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

    ScriptableRenderContext context;
    Camera camera;
    CullingResults cullingResults;
    Lighting lighting = new Lighting();

    //�½�һ��buffer������camerarendereʹ��
    const string bufferName = "Render Single Camera";
    CommandBuffer buffer = new CommandBuffer();

    CommandBuffer sample_Buffer = new CommandBuffer();

    //��RP��������Ⱦ����
    public void Render(ScriptableRenderContext context, Camera camera, bool useDynamicBatching, bool useGPUInstancing)
    {
        this.context = context;
        this.camera = camera;
        PrepareBuffer();
        PrepareForSceneWindow();
        //���޳�
        if (!Cull())
        {
            return;
        }

        //��������Ⱦ��ֵ
        Setup();
        lighting.Setup(context, cullingResults);
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
        DrawUnsupportedShaders();
        DrawGizmos();
        Submit();
    }

    //һЩ��ʼ��������vp����,�����Ǵ�cam��transform�ṹ�ж�ȡ��
    //�ٴ� view�������������ת����cam��project��frustumת��box/ndc
    void Setup()
    {
        context.SetupCameraProperties(camera);
        CameraClearFlags flags = camera.clearFlags;//clear flag����������Ⱦ1 to 4 they are Skybox, Color, Depth, and Nothing
        bool clearDepth = flags <= CameraClearFlags.Depth;
        bool clearColor = flags == CameraClearFlags.Color;
        buffer.ClearRenderTarget(clearDepth, clearColor, clearColor ?
                camera.backgroundColor.linear : Color.clear);
        buffer.BeginSample(SampleName);
        ExecuteBuffer();

    }

    //�����޳�����
    bool Cull()
    {
        //out�ؼ��ֿ���ֱ�Ӷ����ڲ����б���
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
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
            enableInstancing = useGPUInstancing
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


        //͸���������
        using (new FrameDebuggerSampler(this, "Draw Trans and UI"))
        {
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        }

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
