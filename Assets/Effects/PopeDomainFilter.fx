// PopeDomainFilter — 教皇 P3 领域全屏滤镜(Filters.Scene["CalamityEntropy:PopeDomain"])
// 用法: 注册于 EntropySkies.setUpSkies, PopeDomainShaderData 每帧喂 uTargetPosition(领域圆心)/
// uIntensity(领域半径 px)/uZoomCE(GameViewMatrix 缩放)/uOpacity(展开包络)。
// 结构: 域内轻微紫偏色 + 域外去饱和压暗(世界被切断在环外) + 边界过渡带。
sampler uImage0 : register(s0);

float2 uScreenResolution;
float2 uScreenPosition;
float2 uTargetPosition;
float uOpacity;
float uIntensity;  // 领域半径(px)
float uZoomCE;     // 视图缩放(世界坐标换算用)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float4 col = tex2D(uImage0, uv);

    // 屏幕 uv → 世界坐标(补偿缩放: 以屏心为锚)
    float2 halfRes = uScreenResolution * 0.5;
    float2 world = uScreenPosition + halfRes + (uv * uScreenResolution - halfRes) / max(uZoomCE, 0.01);
    float r = distance(world, uTargetPosition) / max(uIntensity, 1);

    float gray = dot(col.rgb, float3(0.3, 0.59, 0.11));

    // 域外: 去饱和 + 压暗(过渡带 r 1.0~1.16)
    float outT = smoothstep(1.0, 1.16, r) * uOpacity;
    float3 outCol = lerp(col.rgb, gray * float3(0.72, 0.64, 0.90), 0.5) * 0.74;
    col.rgb = lerp(col.rgb, outCol, outT);

    // 域内: 轻微紫偏色 + 淡淡的深渊底光
    float inT = (1 - smoothstep(0.90, 1.0, r)) * uOpacity;
    float3 inCol = col.rgb * float3(1.03, 0.945, 1.10) + float3(0.010, 0.002, 0.028);
    col.rgb = lerp(col.rgb, inCol, inT * 0.85);

    return col;
}

technique Technique1
{
    pass PopeDomainPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
