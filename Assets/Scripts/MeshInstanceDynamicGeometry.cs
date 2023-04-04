using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Experimental.Rendering;

[ExecuteInEditMode]
public class MeshInstanceDynamicGeometry : MonoBehaviour
{
    public RayTracingShader rayTracingShader = null;
    public Material material = null;
    public ComputeShader waveAnimationCS = null;
    public Texture envTexture = null;

    private uint meshResolution = 32;

    private uint cameraWidth = 0;
    private uint cameraHeight = 0;

    private Mesh cpuMesh = null;
    private Mesh gpuMesh = null;

    private float realtimeSinceStartup = 0.0f;

    private RenderTexture rayTracingOutput = null;

    private RayTracingAccelerationStructure rtas = null;

    private NativeArray<uint> indexBufferData = new NativeArray<uint>(); 

    private void ReleaseResources()
    {
        if (rtas != null)
        {
            rtas.Release();
            rtas = null;
        }

        if (rayTracingOutput)
        {
            rayTracingOutput.Release();
            rayTracingOutput = null;
        }

        cameraWidth = 0;
        cameraHeight = 0;

        if (cpuMesh != null)
        {
            DestroyImmediate(cpuMesh);
            cpuMesh = null;
        }

        if (gpuMesh != null)
        {
            DestroyImmediate(gpuMesh);
            gpuMesh = null;
        }

        if (indexBufferData.IsCreated)
        {
            indexBufferData.Dispose();
        }
    }

    private NativeArray<uint> CreateIndexArray(uint resolution)
    {
        uint indexCount = 2 * 3 * resolution * resolution;
        var indices = new uint[indexCount];

        int index = 0;

        for (uint i = 0; i < resolution; i++)
            for (uint j = 0; j < resolution; j++)
            {
                indices[index++] = i * (resolution + 1) + j;
                indices[index++] = (i + 1) * (resolution + 1) + j;
                indices[index++] = (i + 1) * (resolution + 1) + j + 1;
                
                indices[index++] = i * (resolution + 1) + j;
                indices[index++] = (i + 1) * (resolution + 1) + j + 1;
                indices[index++] = i * (resolution + 1) + j + 1;
            }

        return new NativeArray<uint>(indices, Allocator.Persistent);
    }

    struct Vertex
    {
        public Vector3 position;
    }

    private NativeArray<Vertex> AnimateVertexArray(uint resolution)
    {
        uint vertexCount = (resolution + 1) * (resolution + 1);

        Vertex[] vertices = new Vertex[vertexCount];

        float invResolution = 1.0f / resolution;

        float step = 2.0f * invResolution;

        int vertexIndex = 0;

        float posZ = -1.0f;

        for (uint z = 0; z <= resolution; z++)
        {
            float posX = -1.0f;

            for (uint x = 0; x <= resolution; x++)
            {
                Vector3 dir = new Vector3(posX, 0, posZ);

                float wave = 0.1f * Mathf.Cos(realtimeSinceStartup * 5 - 10 * dir.magnitude);

                vertices[vertexIndex].position = new Vector3(posX, wave, posZ);

                vertexIndex++;
                posX += step;
            }
            posZ += step;
        }

        return new NativeArray<Vertex>(vertices, Allocator.Temp);
    }

    private NativeArray<Vertex> CreateVertexArray(uint resolution)
    {
        uint vertexCount = (resolution + 1) * (resolution + 1);

        Vertex[] vertices = new Vertex[vertexCount];

        float invResolution = 1.0f / (float)resolution;

        float step = 2.0f * invResolution;

        int vertexIndex = 0;

        float posZ = -1.0f;

        for (uint z = 0; z <= resolution; z++)
        {
            float posX = -1.0f;

            for (uint x = 0; x <= resolution; x++)
            {
                vertices[vertexIndex].position = new Vector3(posX, 0, posZ);

                vertexIndex++;
                posX += step;
            }
            posZ += step;
        }

        return new NativeArray<Vertex>(vertices, Allocator.Temp);
    }

    private void CreateResources()
    {
        if (cameraWidth != Camera.main.pixelWidth || cameraHeight != Camera.main.pixelHeight)
        {
            if (rayTracingOutput)
                rayTracingOutput.Release();

            rayTracingOutput = new RenderTexture(Camera.main.pixelWidth, Camera.main.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            rayTracingOutput.enableRandomWrite = true;
            rayTracingOutput.Create();

            cameraWidth = (uint)Camera.main.pixelWidth;
            cameraHeight = (uint)Camera.main.pixelHeight;
        }
    }

    void OnDestroy()
    {
        ReleaseResources();
    }

    void OnDisable()
    {
        ReleaseResources();
    }

    private void OnEnable()
    {
        if (rtas != null)
            return;

        indexBufferData = CreateIndexArray(meshResolution);

        rtas = new RayTracingAccelerationStructure();

        // 1. CPU Mesh Instance
        {
            if (cpuMesh == null)
            {
                cpuMesh = new Mesh();
                cpuMesh.MarkDynamic();

                using (var varray = CreateVertexArray(meshResolution))
                {
                    cpuMesh.SetVertexBufferParams(varray.Length, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                    cpuMesh.SetVertexBufferData(varray, 0, 0, varray.Length);
                }
             
                cpuMesh.SetIndexBufferParams(indexBufferData.Length, IndexFormat.UInt32);
                cpuMesh.SetIndexBufferData(indexBufferData, 0, 0, indexBufferData.Length);
                cpuMesh.SetSubMesh(0, new SubMeshDescriptor(0, indexBufferData.Length));                

                cpuMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            }

            RayTracingMeshInstanceConfig cpuMeshInstance = new RayTracingMeshInstanceConfig(cpuMesh, 0, material);

            // Make the acceleration structure update every time we call RayTracingAccelerationStructure.Build or CommandBuffer.BuildRayTracingAccelerationStructure.
            cpuMeshInstance.dynamicGeometry = true;

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetTRS(new Vector3(-6.0f, 0.0f, 0.0f), Quaternion.identity, 5 * Vector3.one);

            rtas.AddInstance(cpuMeshInstance, matrix);
        }

        // 2. GPU Mesh Instance
        {
            if (gpuMesh == null)
            {
                gpuMesh = new Mesh();

                if (SystemInfo.supportsComputeShaders)
                {
                    gpuMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                    gpuMesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                }

                using (var varray = CreateVertexArray(meshResolution))
                {
                    gpuMesh.SetVertexBufferParams(varray.Length, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                    gpuMesh.SetVertexBufferData(varray, 0, 0, varray.Length);
                }

                using (var iarray = CreateIndexArray(meshResolution))
                {
                    gpuMesh.SetIndexBufferParams(iarray.Length, IndexFormat.UInt32);
                    gpuMesh.SetIndexBufferData(iarray, 0, 0, iarray.Length);
                    gpuMesh.SetSubMesh(0, new SubMeshDescriptor(0, iarray.Length));
                }
            }

            RayTracingMeshInstanceConfig gpuMeshInstance = new RayTracingMeshInstanceConfig(gpuMesh, 0, material);

            // Make the acceleration structure update every time we call RayTracingAccelerationStructure.Build or CommandBuffer.BuildRayTracingAccelerationStructure.
            gpuMeshInstance.dynamicGeometry = true;

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetTRS(new Vector3(6.0f, 0.0f, 0.0f), Quaternion.identity, 5 * Vector3.one);

            rtas.AddInstance(gpuMeshInstance, matrix);
        }
    }

    void FixedUpdate()
    {
        realtimeSinceStartup += 0.02f;
    }

    [ImageEffectOpaque]
    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (!SystemInfo.supportsRayTracing || !rayTracingShader)
        {
            Debug.Log("The Ray Tracing API is not supported by this GPU or by the current graphics API.");
            Graphics.Blit(src, dest);
            return;
        }

        if (rtas == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        CreateResources();

        CommandBuffer cmdBuffer = new CommandBuffer();
        cmdBuffer.name = "Dynamic Geometry Test";

        // Animate CPU mesh
        Profiler.BeginSample("CPU Mesh Animation");
        {
            using (var varray = AnimateVertexArray(meshResolution))
            {
                cpuMesh.SetVertexBufferParams(varray.Length, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
                cpuMesh.SetVertexBufferData(varray, 0, 0, varray.Length);
            }
        }
        Profiler.EndSample();

        // Animate GPU mesh
        {
            var vertexBuffer = gpuMesh.GetVertexBuffer(0);
            cmdBuffer.SetComputeBufferParam(waveAnimationCS, 0, "vertexBuffer", vertexBuffer);

            cmdBuffer.SetComputeFloatParam(waveAnimationCS, "realtimeSinceStartup", realtimeSinceStartup);
            cmdBuffer.SetComputeIntParam(waveAnimationCS, "vertexCount", vertexBuffer.count);
            cmdBuffer.SetComputeIntParam(waveAnimationCS, "vertexSizeInBytes", vertexBuffer.stride);

            if (vertexBuffer.stride % 4 != 0)
                Debug.Log("Vertex stride must be a multiple of 4.");

            uint kernelGroupSizeX, kernelGroupSizeY, kernelGroupSizeZ;
            waveAnimationCS.GetKernelThreadGroupSizes(0, out kernelGroupSizeX, out kernelGroupSizeY, out kernelGroupSizeZ);

            int threadGroupsX = (int)(gpuMesh.vertexCount + kernelGroupSizeX - 1) / (int)kernelGroupSizeX;
            cmdBuffer.DispatchCompute(waveAnimationCS, 0, threadGroupsX, 1, 1);

            vertexBuffer.Dispose();
        }

        cmdBuffer.BuildRayTracingAccelerationStructure(rtas);

        cmdBuffer.SetRayTracingShaderPass(rayTracingShader, "Test");

        // Input
        cmdBuffer.SetRayTracingAccelerationStructure(rayTracingShader, Shader.PropertyToID("g_AccelStruct"), rtas);
        cmdBuffer.SetRayTracingMatrixParam(rayTracingShader, Shader.PropertyToID("g_InvViewMatrix"), Camera.main.cameraToWorldMatrix);
        cmdBuffer.SetRayTracingFloatParam(rayTracingShader, Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
        cmdBuffer.SetGlobalTexture(Shader.PropertyToID("g_EnvTexture"), envTexture);

        // Output
        cmdBuffer.SetRayTracingTextureParam(rayTracingShader, Shader.PropertyToID("g_Output"), rayTracingOutput);

        cmdBuffer.DispatchRays(rayTracingShader, "MainRayGenShader", cameraWidth, cameraHeight, 1);

        Graphics.ExecuteCommandBuffer(cmdBuffer);

        cmdBuffer.Release();

        Graphics.Blit(rayTracingOutput, dest);
    }
}
