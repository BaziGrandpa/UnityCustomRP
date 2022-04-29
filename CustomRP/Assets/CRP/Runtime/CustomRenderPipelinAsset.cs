using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

//这个类实际只用于存储一些管线数据，而非真正管线实例
[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
public class CustomRenderPipelinAsset : RenderPipelineAsset
{
    [SerializeField]
    bool useDynamicBatching = true, useGPUInstancing = true, useSRPBatcher = true;

    [SerializeField]
    ShadowSettings shadowSetting = default;

    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline(useDynamicBatching, useGPUInstancing, useSRPBatcher, shadowSetting);
    }

}
