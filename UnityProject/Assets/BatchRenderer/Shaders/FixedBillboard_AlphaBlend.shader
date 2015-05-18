﻿Shader "BatchRenderer/FixedBillboard Alpha Blended" {
Properties {
    _MainTex ("Texture", 2D) = "white" {}
    g_base_color ("Base Color", Color) = (1,1,1,1)
}

Category {
    Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
    Blend SrcAlpha OneMinusSrcAlpha
    AlphaTest Greater .01
    ColorMask RGB
    Cull Off Lighting Off ZWrite Off ZTest Always 

    SubShader {
        Pass {
CGPROGRAM
#if defined(SHADER_API_OPENGL)
    #pragma glsl
#elif defined(SHADER_API_D3D9)
    #define BR_WITHOUT_INSTANCE_COLOR
    #pragma target 3.0
    #define BR_WITHOUT_INSTANCE_COLOR
    #define BR_WITHOUT_INSTANCE_EMISSION
#endif
#pragma vertex vert
#pragma fragment frag

#define BR_FIXED_BILLBOARD
#include "Billboard.cginc"
ENDCG
        }
    }
}
}
