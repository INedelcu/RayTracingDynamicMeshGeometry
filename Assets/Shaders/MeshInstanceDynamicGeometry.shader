Shader "RayTracing/MeshInstanceDynamicGeometry"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "DisableBatching" = "True" }
        LOD 100

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag() : SV_Target
            {
                return fixed4(1, 1, 1, 1);
            }

            ENDCG
        }
    }

    SubShader
    {
        Pass
        {
            Name "Test"

            HLSLPROGRAM

            #include "UnityRayTracingMeshUtils.cginc"
            #include "RayPayload.hlsl"
            #include "GlobalResources.hlsl"

            #pragma raytracing some_name

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
#define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                return v;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                float3 worldPosition = mul(ObjectToWorld(), float4(v.position, 1));

                float3 e0 = v1.position - v0.position;
                float3 e1 = v2.position - v0.position;

                float3 faceNormal = normalize(mul(cross(e0, e1), (float3x3)WorldToObject()));

                float3 reflectionVec = reflect(WorldRayDirection(), faceNormal);                

                payload.color = g_EnvTexture.SampleLevel(sampler_g_EnvTexture, reflectionVec, 0).xyz;
            }

            ENDHLSL
        }
    }
}
