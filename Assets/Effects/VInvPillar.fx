// VInvPillar — 魔像光柱着色器(自地面上冲的能量柱)
// 用法: CEFxcEffects.Get("VInvPillar"), SpriteBatch Immediate + Additive + LinearWrap,
// 画一张噪声图(s0)拉成竖条: uv.y=1 柱底(贴地) → 0 柱顶。
// 结构: 亮芯 + 噪声上涌的柱身 + 生长揭示(底向上)与前沿光冠 + 顶端消散。
sampler uImage0 : register(s0);

float uTime;      // 秒级时间
float uGrow;      // 0~1 柱头当前高度(生长动画)
float uOpacity;   // 总强度(爆发首尾包络)
float3 uColorCore; // 芯色(近白)
float3 uColorEdge; // 柱身色(紫)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float dx = uv.x * 2 - 1;
    float h = 1 - uv.y; // 0 柱底 → 1 柱顶

    // 生长揭示: 柱头之上不画; 距柱头越近越亮(上冲光冠)
    if (h > uGrow)
        return float4(0, 0, 0, 0);
    float crown = saturate(1 - (uGrow - h) * 8);

    // 柱身: 两层噪声快速上涌, 侧缘被噪声侵蚀出能量流苏
    float n1 = tex2D(uImage0, float2(dx * 0.42 + 0.5, h * 1.8 - uTime * 1.7)).r;
    float n2 = tex2D(uImage0, float2(dx * 0.21 + 0.87, h * 0.8 - uTime * 0.95)).g;
    float body = saturate(1 - abs(dx) - (n1 * 0.5 + n2 * 0.3) * abs(dx) * 1.5);
    float core = pow(saturate(1 - abs(dx) * 2.3), 2.2);

    float inten = body * 0.7 + core * 1.15;
    inten *= 1 - smoothstep(0.82, 1.0, h) * 0.6; // 顶端消散
    inten += crown * crown * 1.4;                // 前沿光冠
    inten *= uOpacity;
    if (inten <= 0.004)
        return float4(0, 0, 0, 0);

    float3 col = lerp(uColorEdge, uColorCore, saturate(core + crown * 0.8));
    return float4(col * inten, 1);
}

technique Technique1
{
    pass PillarPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
