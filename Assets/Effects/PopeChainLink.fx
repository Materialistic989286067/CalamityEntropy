// PopeChainLink — 教皇死怨铁索链体着色器
// 用法: CEFxcEffects.Get("PopeChainLink"), SpriteBatch Immediate + NonPremultiplied + LinearWrap,
// 画一张噪声图(s0)拉成链条长条: uv.x=0 基点(手) → 1 链头, uv.y 横截。
// 结构: 程序化链环序列(侧视环/立视环交替) + 受力绷直的高光流动 + 底层虚空辉光带 + 崩断噪声侵蚀。
sampler uImage0 : register(s0);

float uTime;       // 秒级时间
float uOpacity;    // 总透明度
float uLinks;      // 全长链环数(按像素长度/24 计)
float uExtend;     // 0~1 伸出进度(链头之外不画)
float uWaveAmp;    // 横向震颤幅度(0~1, 命中/钉入后的绷弦余波)
float uHighlight;  // 张力高光位置(0 基点 → 1 链头; <0 = 无)
float uBreak;      // 0~1 崩断侵蚀进度
float3 uColorDark; // 链环铁色(暗紫黑)
float3 uColorLit;  // 受光缘色(紫)
float3 uColorHot;  // 高光/能量色(亮紫白)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float x = uv.x;
    if (x > uExtend)
        return float4(0, 0, 0, 0);

    // 绷弦余波: 幅度向链头增大的驻波, 快速衰减由 CPU 包络 uWaveAmp 承载
    float wave = sin(x * 22 + uTime * 34) * uWaveAmp * (0.25 + x * 0.75);
    float y = (uv.y * 2 - 1) + wave; // -1~1 横截, 含震颤偏移

    // 金属噪声粒(链面脏度)
    float grain = tex2D(uImage0, float2(x * 5.0, uv.y * 1.7)).r;

    // 链环序列: 偶数环侧视(方环带孔), 奇数环立视(窄棱)
    float linkF = x * uLinks;
    float lx = frac(linkF);
    float alt = fmod(floor(linkF), 2);
    float ay = abs(y);

    float linkMask;
    float3 linkCol;
    if (alt < 0.5)
    {
        // 侧视环: 外框 0.62 内孔 0.30, 环端(lx 边缘)闭合
        float outer = step(ay, 0.62) * step(0.03, lx) * step(lx, 0.97);
        float hole = step(ay, 0.30) * step(0.22, lx) * step(lx, 0.78);
        linkMask = saturate(outer - hole);
        linkCol = uColorDark;
    }
    else
    {
        // 立视环: 窄竖棱, 稍亮(截面反光)
        linkMask = step(ay, 0.34) * step(0.30, lx) * step(lx, 0.70);
        linkCol = lerp(uColorDark, uColorLit, 0.35);
    }

    // 顶缘受光: 上半亮下半沉 + 噪声粒调制
    float topLit = saturate(0.5 - y * 0.9);
    linkCol = lerp(linkCol, uColorLit, topLit * 0.55) * (0.8 + grain * 0.4);

    // 张力高光: 沿链长流动的亮波(受力绷直的读法)
    if (uHighlight >= 0)
    {
        float hl = exp(-abs(x - uHighlight) * 16) * 1.4;
        linkCol += uColorHot * hl;
        linkMask = saturate(linkMask + hl * step(ay, 0.5) * 0.6);
    }

    // 底层虚空辉光带: 链环后面一条窄能量雾(半透, 让锁链嵌在虚空里)
    float glow = saturate(1 - ay * 1.4) * (0.16 + 0.14 * tex2D(uImage0, float2(x * 2 - uTime * 0.8, 0.4)).g);
    float3 col = uColorHot * glow * 0.55;
    float alpha = glow * 0.55;

    // 链环压在辉光上(近实体)
    col = lerp(col, linkCol, linkMask);
    alpha = saturate(alpha + linkMask * 0.96);

    // 崩断侵蚀: 噪声阈值蚕食, 基点侧先碎(能量断供)
    if (uBreak > 0)
    {
        float n2 = tex2D(uImage0, float2(x * 3.1 + 7.7, uv.y * 2.3)).b;
        alpha *= smoothstep(uBreak * 1.25 - 0.2, uBreak * 1.25 + 0.06, n2 * 0.72 + x * 0.28);
    }

    return float4(col, alpha * uOpacity);
}

technique Technique1
{
    pass ChainPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
