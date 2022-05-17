using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

//初步感觉，context的调用顺序决定了渲染顺序，context是一股脑submit的，如果想要立即执行一些gpucmd（不仅仅是渲染指令
//还可以是一些监视指令，状态改变指令等等）
public partial class SingleCameraRenderer
{
    //目前支持的
    static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");
    static ShaderTagId litShaderTagId = new ShaderTagId("CustomLit");
    //如果要开启后处理，几何就渲染在这个id指向的buffer上
    //static int frameBufferId = Shader.PropertyToID("_CameraFrameBuffer");
    static int
        colorAttachmentId = Shader.PropertyToID("_CameraColorAttachment"),
        depthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment"),
        colorTextureId = Shader.PropertyToID("_CameraColorTexture"),
        depthTextureId = Shader.PropertyToID("_CameraDepthTexture"),
        sourceTextureId = Shader.PropertyToID("_SourceTexture");

    //是否会用到深度图，是否要用中介buffer
    //整个流程是这样的如果开启后处理
    //  若开启了后处理，则由后处理的pass将最终图像写入framebuffer
    //  若没开启，则由camerarenderpass写入
    bool useColorTexture, useDepthTexture, useIntermediateBuffer, useHDR;

    ScriptableRenderContext context;
    Camera camera;
    CullingResults cullingResults;
    Lighting lighting = new Lighting();

    //新建一块buffer给单个camerarendere使用
    const string bufferName = "Render Single Camera";
    CommandBuffer buffer = new CommandBuffer();

    CommandBuffer sample_Buffer = new CommandBuffer();

    //后处理
    PostFXStack postFXStack = new PostFXStack();

    Material material;

    //用于处理不开启深度复制的采样source问题
    Texture2D missingTexture;

    static bool copyTextureSupported = SystemInfo.copyTextureSupport > CopyTextureSupport.None;

    public SingleCameraRenderer(Shader shader)
    {
        material = CoreUtils.CreateEngineMaterial(shader);
        missingTexture = new Texture2D(1, 1)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "Missing"
        };
        missingTexture.SetPixel(0, 0, Color.white * 0.5f);
        missingTexture.Apply(true, true);
    }


    //从RP进来的渲染流程
    public void Render(ScriptableRenderContext context, Camera camera, CameraBufferSettings bufferSettings, bool useDynamicBatching, bool useGPUInstancing, ShadowSettings shadowSettings, PostFXSettings postFXSettings)
    {
        this.context = context;
        this.camera = camera;
        PrepareBuffer();
        PrepareForSceneWindow();
        //先剔除
        if (!Cull(shadowSettings.maxDistance))
        {
            return;
        }

        if (camera.cameraType == CameraType.Reflection)
        {
            useColorTexture = bufferSettings.copyColorReflection;
            useDepthTexture = bufferSettings.copyDepthReflection;
        }
        else
        {
            useColorTexture = bufferSettings.copyColor;
            useDepthTexture = bufferSettings.copyDepth;
        }
        useHDR = bufferSettings.allowHDR && camera.allowHDR;

        buffer.BeginSample(SampleName);
        ExecuteBuffer();
        //光照初始化，shadowmap生成
        lighting.Setup(context, cullingResults, shadowSettings);
        //后处理
        postFXStack.Setup(context, camera, postFXSettings);
        buffer.EndSample(SampleName);
        //主要工作在于根据需求设置rendertarget
        Setup();
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
        DrawUnsupportedShaders();
        DrawGizmosBeforeFX();
        if (postFXStack.IsActive)
        {
            //用后处理shader绘制到CameraTarget
            postFXStack.Render(colorAttachmentId);
        }
        else if (useIntermediateBuffer)
        {
            //用普通的复制shader
            Draw(colorAttachmentId, BuiltinRenderTextureType.CameraTarget, false);
            ExecuteBuffer();
        }
        DrawGizmosAfterFX();
        Cleanup();
        Submit();
    }

    //一些初始化，比如vp矩阵,数据是从cam的transform结构中读取的
    //再次 view矩阵将坐标从世界转换到cam，project则将frustum转到box/ndc
    void Setup()
    {
        context.SetupCameraProperties(camera);
        CameraClearFlags flags = camera.clearFlags;//clear flag决定背景渲染1 to 4 they are Skybox, Color, Depth, and Nothing
        //如果要后处理，那么几何就不直接渲染在framebuffer上，而是开辟一个中介
        //主要还是framebuffer不支持直接采样，所以要先渲染到中介上，去采样中介
        bool useFrameBuffer = useColorTexture || useDepthTexture;
        useIntermediateBuffer = postFXStack.IsActive || useFrameBuffer;
        if (useIntermediateBuffer)
        {
            if (flags > CameraClearFlags.Color)
            {
                flags = CameraClearFlags.Color;
            }
            buffer.GetTemporaryRT(
                colorAttachmentId, camera.pixelWidth, camera.pixelHeight,
                0, FilterMode.Bilinear, RenderTextureFormat.Default
            );
            buffer.GetTemporaryRT(
                depthAttachmentId, camera.pixelWidth, camera.pixelHeight,
                32, FilterMode.Point, RenderTextureFormat.Depth
            );
            //分别指定colorbuffer and zbuffer
            buffer.SetRenderTarget(
                colorAttachmentId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                depthAttachmentId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
        }

        bool clearDepth = flags <= CameraClearFlags.Depth;
        bool clearColor = flags == CameraClearFlags.Color;
        buffer.ClearRenderTarget(clearDepth, clearColor, clearColor ?
                camera.backgroundColor.linear : Color.clear);
        buffer.BeginSample(SampleName);
        //先设置默认图，没有开启depthcopy，就会采样missingtexture
        buffer.SetGlobalTexture(colorTextureId, missingTexture);
        buffer.SetGlobalTexture(depthTextureId, missingTexture);
        ExecuteBuffer();
    }

    //初步剔除工作
    bool Cull(float maxShadowDistance)
    {
        //out关键字可以直接定义在参数列表中
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            p.shadowDistance = Mathf.Min(maxShadowDistance, camera.farClipPlane);
            cullingResults = context.Cull(ref p);//这一步是真正剔除，上面只是获取参数
            return true;
        }
        return false;
    }

    //先绘制一些几何
    void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing)
    {
        //不透明物体绘制
        var sortingSettings = new SortingSettings(camera)
        {
            criteria = SortingCriteria.CommonOpaque
        };
        var drawingSettings = new DrawingSettings(
            unlitShaderTagId,
            sortingSettings
        )
        {
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing,
            perObjectData = PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ShadowMask
            //告诉管线，需要传递光照贴图的uv
        };
        drawingSettings.SetShaderPassName(1, litShaderTagId);

        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        //绘制场景中的物体，这里会创建一个drawloop
        context.DrawRenderers(
            cullingResults, ref drawingSettings, ref filteringSettings
        );

        //天空盒       
        using (new FrameDebuggerSampler(this, "Draw Skybox"))
        {
            context.DrawSkybox(camera);
        }

        //在透明物体绘制前复制
        if (useColorTexture || useDepthTexture)
        {
            CopyAttachments();
        }

        //透明物体绘制
        using (new FrameDebuggerSampler(this, "Draw Trans and UI"))
        {
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        }

    }

    //按需复制，以供shader采样
    void CopyAttachments()
    {
        if (useColorTexture)
        {
            buffer.GetTemporaryRT(
                colorTextureId, camera.pixelWidth, camera.pixelHeight,
                0, FilterMode.Bilinear, RenderTextureFormat.Default
            );
            buffer.CopyTexture(colorAttachmentId, colorTextureId);
        }
        if (useDepthTexture)
        {
            buffer.GetTemporaryRT(
                depthTextureId, camera.pixelWidth, camera.pixelHeight,
                32, FilterMode.Point, RenderTextureFormat.Depth
            );
            if (copyTextureSupported)
            {
                buffer.CopyTexture(depthAttachmentId, depthTextureId);
            }
            else
            {
                Draw(depthAttachmentId, depthTextureId, true);
                buffer.SetRenderTarget(
                    colorAttachmentId,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                    depthAttachmentId,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
                );
            }

        }
        ExecuteBuffer();
    }

    //context真正执行
    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();
        context.Submit();
    }

    //context中的cmdBuffer真正执行
    public void ExecuteBuffer(CommandBuffer buffer = null)
    {
        if (buffer == null)
            buffer = this.buffer;

        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    public void SetSampleBufferName(string name)
    {
        sample_Buffer.name = name;
    }

    public CommandBuffer GetSampleBuffer()
    {
        return this.sample_Buffer;
    }

    //这一步将from绘制到to上，目前就final调用下，从中介画到framebuffer
    void Draw(RenderTargetIdentifier from, RenderTargetIdentifier to, bool isDepth = false)
    {
        buffer.SetGlobalTexture(sourceTextureId, from);
        buffer.SetRenderTarget(
            to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
        );
        buffer.DrawProcedural(
            Matrix4x4.identity, material, isDepth ? 1 : 0, MeshTopology.Triangles, 3
        );
    }

    void Cleanup()
    {
        lighting.Cleanup();
        if (postFXStack.IsActive)
        {
            if (useIntermediateBuffer)
            {
                buffer.ReleaseTemporaryRT(colorAttachmentId);
                buffer.ReleaseTemporaryRT(depthAttachmentId);
                if (useDepthTexture)
                {
                    buffer.ReleaseTemporaryRT(depthTextureId);
                }
                if (useColorTexture)
                {
                    buffer.ReleaseTemporaryRT(colorTextureId);
                }
            }
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(material);
        CoreUtils.Destroy(missingTexture);
    }
}
public class FrameDebuggerSampler : IDisposable
{
    SingleCameraRenderer renderer;
    string sample_name;
    CommandBuffer sample_buffer;

    public FrameDebuggerSampler(SingleCameraRenderer renderer, string sampleName)
    {
        this.renderer = renderer;
        this.sample_name = sampleName;
        sample_buffer = renderer.GetSampleBuffer();
        renderer.SetSampleBufferName(sample_name);

        sample_buffer.BeginSample(sampleName);
        renderer.ExecuteBuffer(sample_buffer);
    }

    public void Dispose()
    {
        sample_buffer.EndSample(sample_name);
        renderer.ExecuteBuffer(sample_buffer);
    }
}
