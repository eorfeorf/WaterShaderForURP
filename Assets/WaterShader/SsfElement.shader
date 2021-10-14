Shader "Unlit/SsfElement"
{
    Properties
    {
        _Radius ("Radius", float) = 1
    }
    SubShader
    {
        Pass
        {
            Name "SsfBillboardSphereDepth"
            Tags { "LightMode" = "SsfBillboardSphereDepth" }
            
            HLSLPROGRAM
            #pragma vertex PassVertex
            #pragma fragment PassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Radius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float3 positionVS : TEXCOORD1;
            };

            Varyings PassVertex (Attributes input)
            {
                Varyings output = (Varyings)0;

                output.positionVS =
                    mul(UNITY_MATRIX_MV, float4(0, 0, 0, 1)) // 頂点をオブジェクト座標の原点に.
                    + float4(input.positionOS.x, input.positionOS.y, 0, 0) // カメラ空間を基準に座標を設定.
                    * float4(_Radius * 2, _Radius * 2, 1, 1); // 球体に収まる大きさに拡大
                output.positionCS = mul(UNITY_MATRIX_P, float4(output.positionVS.xyz, 1));
                output.uv = input.uv;
                
                return output;
            }

            float4 PassFragment (Varyings input) : SV_Target
            {
                float2 st = input.uv * 2 - 1;
                half d2 = dot(st, st); // 中心からの2乗の距離.

                // 半径を超える部分は描画しない.
                clip(d2 > 1 ? -1 : 1);

                // 球として法線を計算.
                float3 n = float3(st.xy, sqrt(1 - d2));

                // 球として座標を計算.
                float3 positionVS = float4(input.positionVS + n, 1);
                float4 positionCS = TransformWViewToHClip(positionVS);

                // クリッピング空間の座標をwで除算して正規化デバイス座標に変換.
                float depth = positionCS.z / positionCS.w;
                return depth;
            }
            ENDHLSL
        }
    }
}
