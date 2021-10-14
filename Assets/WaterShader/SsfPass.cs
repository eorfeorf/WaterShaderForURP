using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SsfPass : ScriptableRenderPass
{
    int BlurringIterations => blurringTargetHandles.Length;

    private readonly ProfilingSampler profilingSampler = new ProfilingSampler("Ssf");

    // シェーダーパス.
    private readonly ShaderTagId ssfDepthSahderTagId = new ShaderTagId("SsfBillboardSphereDepth");

    private readonly Material material;

    // 一時的なレンダーテクスチャのID
    private readonly RenderTargetHandle depthTargetHandle;
    private readonly RenderTargetHandle depthNormalTargetHandle;
    private readonly RenderTargetHandle[] blurringTargetHandles;

    private readonly int downSamplingPass;
    private readonly int upSamplingPass;
    private readonly int depthNormalPass;
    private readonly int litPass;

    private RenderTargetIdentifier source;
    private FilteringSettings filteringSettings;

    public SsfPass(
        RenderPassEvent renderPassEvent,
        Material material,
        int blurryIterations,
        LayerMask layerMask,
        RenderQueueRange renderQueueRange)
    {
        // イベントタイミングを設定.
        this.renderPassEvent = renderPassEvent;
        this.material = material;

        // RendererFeatureで設定された内容をもとに、SsfElementだけを対象にする設定を作成.
        filteringSettings = new FilteringSettings(renderQueueRange, layerMask);

        // ブラー用のレンダーターゲットを初期化.
        blurringTargetHandles = new RenderTargetHandle[blurryIterations];
        for (var i = 0; i < blurryIterations; i++)
        {
            blurringTargetHandles[i].Init($"_BlurTemp{i}");
        }

        // 球体の深度値用の一時的なターゲットのIDを初期化.
        depthTargetHandle.Init("_SsfDepthTexture");
        depthNormalTargetHandle.Init("_SsfNormalTexture");

        // パスのインデックスを取得.
        downSamplingPass = material.FindPass("DownSampling");
        upSamplingPass = material.FindPass("UpSampling");
        depthNormalPass = material.FindPass("DepthNormal");
        litPass = material.FindPass("SsfLit");
    }

    public void Setup(RenderTargetIdentifier source)
    {
        this.source = source;
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        // カメラのサイズのRenderTextureを定義.
        var w = cameraTextureDescriptor.width;
        var h = cameraTextureDescriptor.height;

        // RenderTextureを作成.
        var depthTargetDescriptor = new RenderTextureDescriptor(w, h, RenderTextureFormat.RFloat, 0, 0)
        {
            msaaSamples = 1
        };

        cmd.GetTemporaryRT(depthTargetHandle.id, depthTargetDescriptor, FilterMode.Point);

        // ブラー用のレンダーターゲットを作成.
        for (var i = 0; i < BlurringIterations; i++)
        {
            depthTargetDescriptor.width /= 2;
            depthTargetDescriptor.height /= 2;
            cmd.GetTemporaryRT(blurringTargetHandles[i].id, depthTargetDescriptor, FilterMode.Bilinear);
        }

        // 法線用のレンダーターゲット
        var normalTargetDescriptor = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0, 0)
        {
            msaaSamples = 1
        };

        cmd.GetTemporaryRT(depthNormalTargetHandle.id, normalTargetDescriptor, FilterMode.Point);

        // レンダーターゲットをRenderTextureに変更.
        ConfigureTarget(depthTargetHandle.id);
        ConfigureClear(ClearFlag.All, Color.black);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // CommandBufferをプールから取得.
        var cmd = CommandBufferPool.Get(profilingSampler.name);

        //
        // Draw depth
        //

        // SsfElementのみを対象.
        var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
        var drawSettings = CreateDrawingSettings(ssfDepthSahderTagId, ref renderingData, sortFlags);
        drawSettings.perObjectData = PerObjectData.None;

        // カメラに映るオブジェクトをフィルタを指定しつつ描画.
        context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);

        //
        // Blurring
        //

        var currentDestination = depthTargetHandle;

        if (BlurringIterations > 0)
        {
            RenderTargetHandle currentSource;

            // Down sampling
            // 0->1->2
            for (var i = 0; i < BlurringIterations; ++i)
            {
                currentSource = currentDestination;
                currentDestination = blurringTargetHandles[i];
                cmd.Blit(currentSource.id, currentDestination.id, material, downSamplingPass);
            }

            // Up sampling
            // すでにUpsamplingの最後のブラーがターゲットに入るため-2をしている（自分自身に書き込んでしまうため）
            // 1->0
            for (var i = BlurringIterations - 2; i >= 0; --i)
            {
                currentSource = currentDestination;
                currentDestination = blurringTargetHandles[i];

                cmd.Blit(currentSource.id, currentDestination.id, material, upSamplingPass);
            }
        }

        // Draw normal
        var clipToView = GL.GetGPUProjectionMatrix(renderingData.cameraData.camera.projectionMatrix, true).inverse;
        cmd.SetGlobalMatrix("_MatrixClipToView", clipToView);
        cmd.Blit(currentDestination.id, depthNormalTargetHandle.id, material, depthNormalPass);

        // Lighting
        cmd.SetGlobalTexture("_SsfDepthNormalTexture", depthNormalTargetHandle.id);
        cmd.Blit(source, source, material, litPass);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        cmd.ReleaseTemporaryRT(depthTargetHandle.id);
        cmd.ReleaseTemporaryRT(depthNormalTargetHandle.id);
        foreach (var targetHandle in blurringTargetHandles)
        {
            cmd.ReleaseTemporaryRT(targetHandle.id);
        }
    }
}