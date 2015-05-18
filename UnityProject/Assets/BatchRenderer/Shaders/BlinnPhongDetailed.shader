﻿Shader "BatchRenderer/BlinnPhong Detailed" {
Properties {
    g_base_color ("Base Color", Color) = (1,1,1,1)
    g_base_emission ("Emission", Color) = (0,0,0,0)
    _MainTex ("Base (RGB)", 2D) = "white" {}
    _NormalMap ("Normalmap", 2D) = "bump" {}
    _EmissionMap ("Emissionmap", 2D) = "black" {}
    _SpecularMap ("Specularmap", 2D) = "black" {}
    _GrossMap ("Grossmap", 2D) = "black" {}
}
SubShader {
    Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }

CGPROGRAM
#if defined(SHADER_API_OPENGL)
    #pragma glsl
#elif defined(SHADER_API_D3D9)
    #define BR_WITHOUT_INSTANCE_COLOR
    #define BR_WITHOUT_INSTANCE_EMISSION
    #pragma target 3.0
#endif
#pragma surface surf BlinnPhong vertex:vert addshadow

#define BR_SURFACE_DETAILED
#include "Surface.cginc"
ENDCG
}

Fallback Off
}
