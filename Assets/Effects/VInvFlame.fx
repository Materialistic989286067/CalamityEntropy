// VInvFlame — 虚空喷焰火舌着色器(烛灵/爬行者/教皇蠕虫同源)
// 用法: CEEffectAssets.VInvFlame, SpriteBatch Immediate + Additive + LinearWrap,
// 画一张噪声图(s0)拉成锥形长条: uv.x=0 喷口 → 1 远端, uv.y 横截。
// 结构: 双层噪声沿轴外流 + 锥形口径蒙版 + 噪声侵蚀出火舌 + 三段色温渐变。
sampler uImage0 : register(s0);

float uTime;      // 秒级时间
float uReach;     // 0~1 火舌当前伸展(起手生长/收尾回缩)
float uOpacity;   // 总强度
float3 uColorCore; // 芯色(近白亮紫)
float3 uColorMid;  // 中段(亮紫)
float3 uColorEdge; // 边缘/尾段(深紫)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float x = uv.x;
    float dy = uv.y * 2 - 1;

    // 锥形口径: 近窄远宽(与判定的 10→90px 半径线性放大对齐), 远端 15% 略收出尖
    float halfW = lerp(0.14, 1.0, smoothstep(0.0, 0.85, x)) * (1 - smoothstep(0.85, 1.0, x) * 0.35);

    // 双层噪声沿轴向外滚动(负向=离开喷口), 第二层慢而大, 火有内外两速
    float n1 = tex2D(uImage0, float2(x * 1.7 - uTime * 1.5, dy * 0.4 + uTime * 0.23)).r;
    float n2 = tex2D(uImage0, float2(x * 0.8 - uTime * 0.85, dy * 0.2 - uTime * 0.12)).g;
    float noise = n1 * 0.6 + n2 * 0.4;

    // 火体 = 截面蒙版被噪声侵蚀; 越靠远端侵蚀越狠 → 撕出火舌
    float body = saturate(1 - abs(dy) / max(halfW, 0.001));
    float flame = saturate(body - noise * (0.4 + x * 0.85) + 0.3);

    // 伸展包络: uReach 之外裁掉, 前沿羽化(火头是软的)
    flame *= smoothstep(1.05, 0.6, x / max(uReach, 0.001));
    float heat = pow(flame, 1.7) * (1 - x * 0.3);
    if (heat <= 0.003)
        return float4(0, 0, 0, 0);

    // 三段色温: 边缘深紫 → 中段亮紫 → 芯部近白
    float3 col = lerp(uColorEdge, uColorMid, saturate(heat * 1.7));
    col = lerp(col, uColorCore, saturate((heat - 0.5) * 2.6));
    return float4(col * heat * uOpacity, 1);
}

technique Technique1
{
    pass FlamePass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
