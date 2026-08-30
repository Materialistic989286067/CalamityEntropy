// PopeBeam — 教皇系贯穿激光束体着色器(魔盘巨激光/爆弹放射激光/反射激光共用)
// 用法: CEEffectAssets.PopeBeam, SpriteBatch Immediate + Additive + LinearWrap,
// 画一张噪声图(s0)拉成横条: uv.x=0 出膛端 → 1 远端, uv.y 横截。
// 结构: 白核 + 色晕 + 噪声热浪扰动边缘 + 出膛端过曝 + 远端轻收。
sampler uImage0 : register(s0);

float uTime;       // 秒级时间
float uOpacity;    // 总强度(渐灭包络)
float uCore;       // 白核半宽(0~1, 相对束半宽)
float uFringe;     // 边缘热浪扰动幅度(0~1)
float uGrow;       // 0~1 宽度展开包络(出膛几帧不满宽的公平阀)
float uFlicker;    // 亮度闪烁幅度(0~1)
float3 uColorCore; // 核色(近白)
float3 uColorHalo; // 晕色(紫)
float3 uColorEdge; // 最外缘色(深紫)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float x = uv.x;
    float dy = abs(uv.y * 2 - 1) / max(uGrow, 0.02);

    // 双层噪声沿束轴高速外流(热浪), 边缘被其扰动
    float n1 = tex2D(uImage0, float2(x * 2.2 - uTime * 3.6, uv.y * 0.9 + uTime * 0.5)).r;
    float n2 = tex2D(uImage0, float2(x * 1.1 - uTime * 2.1, uv.y * 0.5 - uTime * 0.3)).g;
    float fringe = (n1 * 0.65 + n2 * 0.35) * uFringe;

    float body = saturate(1 - dy - fringe * dy * 1.6);
    float core = pow(saturate(1 - dy / max(uCore, 0.03)), 2.0);

    float inten = body * 0.7 + core * 1.55;
    inten *= 1 + exp(-x * 9) * 0.9;                 // 出膛端过曝
    inten *= 1 - smoothstep(0.90, 1.0, x) * 0.45;   // 远端轻收
    inten *= 1 + (n1 - 0.5) * uFlicker;             // 热闪
    inten *= uOpacity;
    if (inten <= 0.004)
        return float4(0, 0, 0, 0);

    float3 col = lerp(uColorEdge, uColorHalo, saturate(body));
    col = lerp(col, uColorCore, saturate(core));
    return float4(col * inten, 1);
}

technique Technique1
{
    pass BeamPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
