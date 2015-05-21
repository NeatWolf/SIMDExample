﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


public class PEParticles : MonoBehaviour
{
    [StructLayout(LayoutKind.Explicit)]
    public struct peParticle
    {
        public const int size = 32;

        [FieldOffset(0)]  public Vector3 position;
        [FieldOffset(0)]  public Vector4 position4;
        [FieldOffset(16)] public Vector3 velocity;
        [FieldOffset(16)] public Vector4 velocity4;
    }
    public delegate void CSUpdateRoutine(float dt, int begin, int end);

    public enum peUpdateRoutine
    {
        Plain,
        SIMD,
        SIMDSoA,
        ISPC,
        CSharp,
        ComputeShader,
    }

    public struct peParams
    {
        public peUpdateRoutine routine;
        public bool multi_threading;
        public float particle_size;
        public float pressure_stiffness;
        public float wall_stiffness;
        public CSUpdateRoutine update_velocity;
        public CSUpdateRoutine update_position;
    }

    [DllImport ("ParticleEngine")] public static extern IntPtr  peCreateContext(int num);
    [DllImport ("ParticleEngine")] public static extern void    peDestroyContext(IntPtr ctx);

    [DllImport ("ParticleEngine")] public static extern void    peSetParams(IntPtr ctx, ref peParams v);
    [DllImport ("ParticleEngine")] public static extern void    peUpdate(IntPtr ctx, float dt);
    [DllImport ("ParticleEngine")] public static extern void    peCopyDataToTexture(IntPtr ctx, IntPtr texture, int width, int height);
    [DllImport ("ParticleEngine")] unsafe public static extern peParticle* peGetParticles(IntPtr ctx);

    [DllImport ("ParticleEngine")] public static extern IntPtr  peBenchmark(IntPtr ctx, int loop_count);



    public struct CSParams
    {
        public const int size = 32;

        public int particle_count;
        public float particle_size;
        public float rcp_particle_size2;
        public float pressure_stiffness;
        public float wall_stiffness;
        public float timestep;

        float pad1, pad2;
    };


    public const int DataTextureWidth = 128;
    const int KernelBlockSize = 256;

    public peUpdateRoutine m_routine = peUpdateRoutine.ISPC;
    public bool m_multi_threading = true;
    public int m_particle_count = 1024*4;
    public float m_particle_size = 0.1f;
    public float m_pressure_stiffness = 500.0f;
    public float m_wall_stiffness = 1500.0f;

    public ComputeShader m_cs_particle_core;
    public ComputeBuffer m_cb_params;
    public ComputeBuffer m_cb_particles;
    CSParams[] m_csparams;

    IntPtr m_ctx;



    public void SetUpdateRoutibe(int v)
    {
        m_routine = (peUpdateRoutine)v;
    }

    public void SetParticleCount(int v)
    {
        m_particle_count = v;
        ResetContext();
    }

    public void EnableMultiThreading(bool v)
    {
        m_multi_threading = v;
    }

    public void CopyDataToTexture(RenderTexture tex)
    {
        peCopyDataToTexture(m_ctx, tex.GetNativeTexturePtr(), tex.width, tex.height);
    }


    void ReleaseContext()
    {
        if (m_ctx != IntPtr.Zero)
        {
            peDestroyContext(m_ctx);
            m_ctx = IntPtr.Zero;
        }
        if (m_cb_params != null)
        {
            m_cb_params.Release(); m_cb_params = null;
            m_cb_particles.Release(); m_cb_particles = null;
        }
    }

    void ResetContext()
    {
        ReleaseContext();

        m_particle_count = Mathf.Max(DataTextureWidth, m_particle_count);
        m_ctx = peCreateContext(m_particle_count);

        if (SystemInfo.supportsComputeShaders)
        {
            m_cb_params = new ComputeBuffer(1, CSParams.size);
            m_cb_particles = new ComputeBuffer(m_particle_count, peParticle.size);
            m_csparams = new CSParams[1];
            {
                var tmp = new peParticle[m_particle_count];
                for (int i = 0; i < tmp.Length; ++i )
                {
                    tmp[i].position = new Vector3(
                        UnityEngine.Random.Range(-5.0f, 5.0f),
                        UnityEngine.Random.Range(-5.0f, 5.0f) + 5.0f,
                        UnityEngine.Random.Range(-5.0f, 5.0f) );
                }
                m_cb_particles.SetData(tmp);
            }
            for (int i = 0; i < 2; ++i )
            {
                m_cs_particle_core.SetBuffer(i, "g_params", m_cb_params);
                m_cs_particle_core.SetBuffer(i, "g_particles", m_cb_particles);
            }
        }
    }


    void OnEnable()
    {
        ResetContext();
    }

    void OnDisable()
    {
        ReleaseContext();
        m_ctx = IntPtr.Zero;
    }

    void Update()
    {
        if(m_routine==peUpdateRoutine.ComputeShader)
        {
            Update_ComputeShader();
        }
        else
        {
            PassParametersToPlugin();
            peUpdate(m_ctx, Time.deltaTime);
        }
    }

    public void PassParametersToPlugin()
    {
        peParams p;
        p.routine = m_routine;
        p.multi_threading = m_multi_threading;
        p.pressure_stiffness = m_pressure_stiffness;
        p.wall_stiffness = m_wall_stiffness;
        p.particle_size = m_particle_size;
        p.update_velocity = Update_Velocity;
        p.update_position = Update_Position;
        peSetParams(m_ctx, ref p);
    }

    unsafe void Update_Velocity(float dt, int begin, int end)
    {
        peParticle *particles = peGetParticles(m_ctx);
        float particle_size2 = m_particle_size * 2.0f;
        float rcp_particle_size2 = 1.0f / (m_particle_size * 2.0f);

        // パーティクル同士の押し返し
        for (int i = begin; i < end; ++i) {
            Vector3 pos1 = particles[i].position;
            Vector3 accel = Vector3.zero;
            for (int j = 0; j < m_particle_count; ++j) {
                Vector3 pos2 = particles[j].position;
                Vector3 diff = pos2 - pos1;
                Vector3 dir = diff * rcp_particle_size2;
                float dist = diff.magnitude;
                if (dist > 0.0f) {
                    Vector3 a = dir * (Mathf.Min(0.0f, dist - particle_size2) * m_pressure_stiffness);
                    accel = accel + a;
                }
            }

            Vector3 vel = particles[i].velocity;
            vel = vel + accel * dt;
            particles[i].velocity = vel;
        }

        // 床との衝突
        Vector3 floor_normal = new Vector3(0.0f, 1.0f, 0.0f);
        float floor_distance = -m_particle_size;
        for (int i = begin; i < end; ++i) {
            Vector3 pos = particles[i].position;
            float d = Vector3.Dot(pos, floor_normal) + floor_distance;
            Vector3 accel = floor_normal * (-Mathf.Min(0.0f, d) * m_wall_stiffness);
            Vector3 vel = particles[i].velocity;
            vel = vel + accel * dt;
            particles[i].velocity = vel;
        }

        // 重力加速
        Vector3 gravity_direction = new Vector3(0.0f, -1.0f, 0.0f);
        float gravity_strength = 5.0f;
        for (int i = begin; i < end; ++i) {
            Vector3 accel = gravity_direction * gravity_strength;
            Vector3 vel = particles[i].velocity;
            vel = vel + accel * dt;
            particles[i].velocity = vel;
        }
    }

    unsafe void Update_Position(float dt, int begin, int end)
    {
        peParticle *particles = peGetParticles(m_ctx);
        for (int i = begin; i < end; ++i) {
            Vector3 pos = particles[i].position;
            Vector3 vel = particles[i].velocity;
            pos = pos + (vel * dt);
            particles[i].position = pos;
        }
    }

    void Update_ComputeShader()
    {
        if(!SystemInfo.supportsComputeShaders)
        {
            Debug.Log("ComputeShader is not available.");
            return;
        }

        m_csparams[0].particle_count = m_particle_count;
        m_csparams[0].particle_size = m_particle_size;
        m_csparams[0].rcp_particle_size2 = 1.0f / (m_particle_size * 2.0f);
        m_csparams[0].pressure_stiffness = m_pressure_stiffness;
        m_csparams[0].wall_stiffness = m_wall_stiffness;
        m_csparams[0].timestep = Time.deltaTime;
        m_cb_params.SetData(m_csparams);

        m_cs_particle_core.Dispatch(0, m_particle_count/KernelBlockSize, 1, 1);
        m_cs_particle_core.Dispatch(1, m_particle_count/KernelBlockSize, 1, 1);
    }

    public void Benchmark()
    {
        PassParametersToPlugin();
        IntPtr str = peBenchmark(m_ctx, 1);
        string result = Marshal.PtrToStringAnsi(str);
        Debug.Log(result);
    }
}
