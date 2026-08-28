// PopeSlashArc — 教皇魔镰旋斩刀光着色器
// 用法: CEFxcEffects.Get("PopeSlashArc"), SpriteBatch Immediate + NonPremultiplied + LinearWrap,
// 画一张噪声图(s0)撑满以斩击中心为心的正方形, 片元内换极坐标画月牙。
// 结构: 前缘锐(白热刃线) + 腹部鼓(高斯厚度) + 后缘散(内侧噪声撕散) + 尾迹急衰(方向读法) + 定格后噪声侵蚀消散。
sampler uImage0 : register(s0);

float uTime;       // 秒级时间
float uOpacity;    // 总透明度
float uFront;      // 刃前缘当前角(弧度, 世界系)
float uSpan;       // 可见拖尾弧长(弧度)
float uDir;        // 旋向(+1/-1)
float uOuter;      // 外缘半径(0~1, 相对画布半宽)
float uWidthMax;   // 最大带厚(0~1)
float uFade;       // 0~1 定格后消散进度
float uHot;        // 前缘热度(斩击拍 1 → 定格衰减)
float3 uColorEdge; // 刃线色(白热)
float3 uColorBody; // 刀光主体色(紫)
float3 uColorDeep; // 内缘暗色(深紫)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = uv * 2 - 1;
    float r = length(p);
    if (r > 1)
        return float4(0, 0, 0, 0);
    float a = atan2(p.y, p.x);

    // 相对前缘的角距(0 = 前缘, 正向为拖尾侧)
    float d = (uFront - a) * uDir;
    d = d - 6.2831853 * floor(d / 6.2831853); // 0~2pi
    float rel = d / uSpan;                    // 0 前缘 → 1 尾端
    if (rel > 1)
        return float4(0, 0, 0, 0);

    // 厚度: 前缘薄(锐) → 腹部鼓(高斯 0.35 处) → 尾端收
    float belly = exp(-pow((rel - 0.35) / 0.30, 2));
    float width = uWidthMax * (0.16 + 0.84 * belly);
    float outer = uOuter * (1 - 0.06 * rel);
    float inner = outer - width;
    if (r > outer || r < inner - 0.08)
        return float4(0, 0, 0, 0);
    float t = saturate((r - inner) / max(width, 0.001)); // 0 内缘 → 1 外缘

    // 主体噪声(侵蚀 + 质感共用)
    float n = tex2D(uImage0, float2(a * 0.9 + uTime * 0.23, r * 2.6)).r;
    float n2 = tex2D(uImage0, float2(a * 1.7 - 3.3, r * 1.3 + 5.1)).g;

    // 主体: 内暗外亮; 尾迹急衰(任意瞬间读作有向月牙, 不是量角器环)
    float tailFade = pow(saturate(1 - rel), 1.6);
    float body = smoothstep(0, 0.24 + n * 0.30, t) * (0.55 + 0.45 * n) * tailFade;
    float3 col = lerp(uColorDeep, uColorBody, t * t);

    // 结构白: 前缘刃线 + 外缘亮棱(只挂在新鲜段)
    float edgeGlow = exp(-rel * uSpan * 7.5) * 1.7;
    float rim = exp(-abs(t - 0.90) * 9) * tailFade * 0.9;
    float hot = (edgeGlow + rim) * uHot;
    col = lerp(col, uColorEdge, saturate(hot));
    float inten = body + hot * 0.9;

    // 定格后消散: 噪声阈值侵蚀, 尾端先散、前缘最后熄
    float alpha = saturate(inten);
    if (uFade > 0)
    {
        alpha *= smoothstep(uFade * 1.35 - 0.22, uFade * 1.35 + 0.08, n2 * 0.62 + (1 - rel) * 0.38);
    }

    return float4(col * saturate(inten * 1.15), alpha * uOpacity);
}

technique Technique1
{
    pass SlashPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
