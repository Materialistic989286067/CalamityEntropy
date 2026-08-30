// PopeDomainRing — 教皇 P3 领域边界环带着色器
// 用法: CEEffectAssets.PopeDomainRing, SpriteBatch Immediate + Additive + LinearWrap,
// 画一张噪声图(s0)撑满以领域圆心为心的正方形(边长略大于直径), 片元内换极坐标画环。
// 结构: 裂隙纹理双向流动 + 亮丝裂纹 + 白芯读线 + 内外双色域(内壁泛光/外侧沉暗紫) + 预警脉动/死亡白闪调制。
sampler uImage0 : register(s0);

float uTime;       // 秒级时间
float uOpacity;    // 总强度
float uRadius;     // 环半径(0~1, 相对画布半宽)
float uThick;      // 环带半厚(0~1)
float uPulse;      // 0~1 预警脉动强度(安全区轮转预警窗)
float uCrackFlash; // 0~1 死亡龟裂白闪强度
float3 uColorEdge; // 环体色(紫)
float3 uColorCore; // 芯线色(亮紫白)
float3 uColorIn;   // 内壁泛光色(偏粉紫)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = uv * 2 - 1;
    float r = length(p);
    float dr = (r - uRadius) / max(uThick, 0.001); // 带内 -1~1, 内负外正
    if (abs(dr) > 4 && r < uRadius - 0.16)
        return float4(0, 0, 0, 0);
    if (dr > 4)
        return float4(0, 0, 0, 0);
    float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;

    // 裂隙纹理: 两层噪声沿环反向异速流动(空间被撕开的活感)
    float n1 = tex2D(uImage0, float2(ang * 6 + uTime * 0.055, r * 2.2 - uTime * 0.10)).r;
    float n2 = tex2D(uImage0, float2(-ang * 9 + uTime * 0.032, r * 4.1 + uTime * 0.07)).g;
    float rift = n1 * 0.6 + n2 * 0.4;

    // 环体: 高斯软墙, 裂隙调制明暗; 亮丝裂纹 = 噪声等值线
    float wall = exp(-dr * dr * 1.05) * (0.45 + rift * 0.85);
    float ridge = pow(saturate(1 - abs(rift - 0.5) * 6.5), 3) * saturate(1 - abs(dr) * 0.55);
    float coreLine = exp(-dr * dr * 15) * 1.35;

    // 内壁泛光(内侧域感), 外侧快速沉没
    float innerGlow = 0;
    if (dr < 0)
        innerGlow = exp(dr * 0.9) * saturate(1 + dr * 0.22) * 0.32 * (0.6 + rift * 0.7);

    float inten = wall * 0.8 + ridge * 1.25 + coreLine + innerGlow;
    inten *= 1 + uPulse * 0.75;
    inten *= uOpacity;
    if (inten <= 0.004)
        return float4(0, 0, 0, 0);

    float3 col = lerp(uColorEdge, uColorCore, saturate(coreLine + ridge * 0.5));
    if (dr < 0)
        col = lerp(col, uColorIn, saturate(-dr * 0.4) * 0.55);
    // 死亡龟裂: 裂纹亮丝闪白
    col = lerp(col, float3(1, 1, 1), saturate(uCrackFlash * (ridge * 1.6 + 0.15)));

    return float4(col * inten, 1);
}

technique Technique1
{
    pass RingPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
