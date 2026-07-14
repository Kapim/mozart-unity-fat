// Opaque, always-on-top preview material for the portal placement box, with highlighted
// edges so the box shape reads clearly (a plain flat colour blends together).
//
// Render state matches Custom/AlwaysVisibleContentUnlit (Blend One Zero, ZTest Always,
// Overlay queue), which is confirmed visible everywhere over passthrough. The only addition
// is UV-based edge detection: each cube face is tinted _FaceColor and framed with _EdgeColor.
Shader "Custom/PortalBoxEdges"
{
    Properties
    {
        _FaceColor ("Face Color", Color) = (0.10, 0.45, 0.65, 1)
        _EdgeColor ("Edge Color", Color) = (0.6, 1.0, 1.0, 1)
        _EdgeWidth ("Edge Width", Range(0, 0.5)) = 0.06
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            Blend One Zero
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _FaceColor;
                float4 _EdgeColor;
                float _EdgeWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Distance to the nearest face edge (0 at edge, 0.5 at face center).
                float2 d = min(input.uv, 1.0 - input.uv);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, min(d.x, d.y));
                half3 rgb = lerp(_FaceColor.rgb, _EdgeColor.rgb, edge);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
