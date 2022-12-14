# RayTracingDynamicMeshGeometry
Unity sample project using dynamic Mesh geometries in a RayTracingAccelerationStructure (RTAS).

<img src="Images/GameView.png" width="1280">

## Description
The project uses [RayTracingAccelerationStructure.AddInstance](https://docs.unity3d.com/2023.1/Documentation/ScriptReference/Rendering.RayTracingAccelerationStructure.AddInstance.html) function to add ray tracing instances that use dynamic geometries to the RTAS. The function signature used is:

`int AddInstance(ref Rendering.RayTracingMeshInstanceConfig config, Matrix4x4 matrix, Nullable<Matrix4x4> prevMatrix, uint id);`
\
\
There are 2 dynamic Meshes in the Scene - the left Mesh is animated on the CPU in C# while the right one on the GPU in a [compute shader](Assets/Shaders/WaveMeshAnimation.compute). Check [MeshInstanceDynamicGeometry.cs](Assets/Scripts/MeshInstanceDynamicGeometry.cs). After animation, [CommandBuffer.BuildRayTracingAccelerationStructure](https://docs.unity3d.com/2023.1/Documentation/ScriptReference/Rendering.CommandBuffer.BuildRayTracingAccelerationStructure.html) is called where the acceleration structures (BLAS) associated with the 2 geometries are built on the GPU.

The ray generation shader in [MeshInstanceDynamicGeometry.raytrace](Assets/Shaders/MeshInstanceDynamicGeometry.raytrace) casts the rays into the view frustum by calling [TraceRay](https://learn.microsoft.com/en-us/windows/win32/direct3d12/traceray-function) and generates the resulting image into a RenderTexture which is displayed in the Game View output.

The hit shader used by the 2 geometries is [MeshInstanceDynamicGeometry.shader](Assets/Shaders/MeshInstanceDynamicGeometry.shader), second SubShader.

After opening the project, switch to Game view and press Play to animate the geometries.

## Prerequisites

* Windows 10 version 1809 and above.
* GPU supporting Ray Tracing ([SystemInfo.supportsRayTracing](https://docs.unity3d.com/2023.1/Documentation/ScriptReference/SystemInfo-supportsRayTracing.html) must be true).
* Unity 2023.1.0a3+.

## Resources
* [DirectX Raytracing (DXR) specs](https://microsoft.github.io/DirectX-Specs/d3d/Raytracing.html)
* [Unity Forum](https://forum.unity.com)
