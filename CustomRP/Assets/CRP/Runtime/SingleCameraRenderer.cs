using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

//�����о���context�ĵ���˳���������Ⱦ˳��context��һ����submit�ģ������Ҫ����ִ��һЩgpucmd������������Ⱦָ��
//��������һЩ����ָ�״̬�ı�ָ��ȵȣ�
public partial class SingleCameraRenderer
{
    //Ŀǰ֧�ֵ�
    static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");
    static ShaderTagId litShaderTagId = new ShaderTagId("CustomLit");
    //���Ҫ�����������ξ���Ⱦ�����idָ���buffer��
    //static int frameBufferId = Shader.PropertyToID("_CameraFrameBuffer");
    static int
        colorAttachmentId = Shader.PropertyToID("_CameraColorAttachment"),
        depthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment"),
        colorTextureId = Shader.PropertyToID("_CameraColorTexture"),
        depthTextureId = Shader.PropertyToID("_CameraDepthTexture"),
        sourceTextureId = Shader.PropertyToID("_SourceTexture");

    //�Ƿ���õ����ͼ���Ƿ�Ҫ���н�buffer
    //���������������������������
    //  �������˺������ɺ����pass������ͼ��д��framebuffer
    //  ��û����������camerarenderpassд��
    bool useColorTexture, useDepthTexture, useIntermediateBuffer, useHDR;

    ScriptableRenderContext context;
    Camera camera;
    CullingResults cullingResults;
    Lighting lighting = new Lighting();

    //�½�һ��buffer������camerarendereʹ��
    const string bufferName = "Render Single Camera";
    CommandBuffer buffer = new CommandBuffer();

    CommandBuffer sample_Buffer = new CommandBuffer();

    //����
    PostFXStack postFXStack = new PostFXStack();

    Material material;

    //���ڴ���������ȸ��ƵĲ���source����
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


    //��RP��������Ⱦ����
    public void Render(ScriptableRenderContext context, Camera camera, CameraBufferSettings bufferSettings, bool useDynamicBatching, bool useGPUInstancing, ShadowSettings shadowSettings, PostFXSettings postFXSettings)
    {
        this.context = context;
        this.camera = camera;
        PrepareBuffer();
        PrepareForSceneWindow();
        //���޳�
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
        //���ճ�ʼ����shadowmap����
        lighting.Setup(context, cullingResults, shadowSettings);
        //����
        postFXStack.Setup(context, camera, postFXSettings);
        buffer.EndSample(SampleName);
        //��Ҫ�������ڸ�����������rendertarget
        Setup();
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
        DrawUnsupportedShaders();
        DrawGizmosBeforeFX();
        if (postFXStack.IsActive)
        {
            //�ú���shader���Ƶ�CameraTarget
            postFXStack.Render(colorAttachmentId);
        }
        else if (useIntermediateBuffer)
        {
            //����ͨ�ĸ���shader
            Draw(colorAttachmentId, BuiltinRenderTextureType.CameraTarget, false);
            ExecuteBuffer();
        }
        DrawGizmosAfterFX();
        Cleanup();
        Submit();
    }

    //һЩ��ʼ��������vp����,�����Ǵ�cam��transform�ṹ�ж�ȡ��
    //�ٴ� view�������������ת����cam��project��frustumת��box/ndc
    void Setup()
    {
        context.SetupCameraProperties(camera);
        CameraClearFlags flags = camera.clearFlags;//clear flag����������Ⱦ1 to 4 they are Skybox, Color, Depth, and Nothing
        //���Ҫ������ô���ξͲ�ֱ����Ⱦ��framebuffer�ϣ����ǿ���һ���н�
        //��Ҫ����framebuffer��֧��ֱ�Ӳ���������Ҫ����Ⱦ���н��ϣ�ȥ�����н�
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
            //�ֱ�ָ��colorbuffer and zbuffer
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
        //������Ĭ��ͼ��û�п���depthcopy���ͻ����missingtexture
        buffer.SetGlobalTexture(colorTextureId, missingTexture);
        buffer.SetGlobalTexture(depthTextureId, missingTexture);
        ExecuteBuffer();
    }

    //�����޳�����
    bool Cull(float maxShadowDistance)
    {
        //out�ؼ��ֿ���ֱ�Ӷ����ڲ����б���
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            p.shadowDistance = Mathf.Min(maxShadowDistance, camera.farClipPlane);
            cullingResults = context.Cull(ref p);//��һ���������޳�������ֻ�ǻ�ȡ����
            return true;
        }
        return false;
    }

    //�Ȼ���һЩ����
    void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing)
    {
        //��͸���������
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
            //���߹��ߣ���Ҫ���ݹ�����ͼ��uv
        };
        drawingSettings.SetShaderPassName(1, litShaderTagId);

        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        //���Ƴ����е����壬����ᴴ��һ��drawloop
        context.DrawRenderers(
            cullingResults, ref drawingSettings, ref filteringSettings
        );

        //��պ�       
        using (new FrameDebuggerSampler(this, "Draw Skybox"))
        {
            context.DrawSkybox(camera);
        }

        //��͸���������ǰ����
        if (useColorTexture || useDepthTexture)
        {
            CopyAttachments();
        }

        //͸���������
        using (new FrameDebuggerSampler(this, "Draw Trans and UI"))
        {
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        }

    }

    //���踴�ƣ��Թ�shader����
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

    //context����ִ��
    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();
        context.Submit();
    }

    //context�е�cmdBuffer����ִ��
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

    //��һ����from���Ƶ�to�ϣ�Ŀǰ��final�����£����н黭��framebuffer
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
