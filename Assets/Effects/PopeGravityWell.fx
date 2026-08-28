// PopeGravityWell — 死亡爆弹引力扭曲着色器(伪透镜)
// 用法: CEFxcEffects.Get("PopeGravityWell"), SpriteBatch Immediate + NonPremultiplied + LinearWrap,
// 画一张噪声图(s0)撑满以爆弹为心的正方形(边长约爆弹直径 4~5 倍), 片元内极坐标自算。
// 结构: 半径反比的内旋涡丝(空间被拽弯) + 向心行进的压缩环纹 + 吸积亮缘 + 中心域压暗(光被吞)。
sampler uImage0 : register(s0);

float uTime;      // 秒级时间
float uOpacity;   // 总透明度
float uStrength;  // 0~1 引力强度(随充能爬升)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = uv * 2 - 1;
    float r = length(p);
    if (r > 1)
        return float4(0, 0, 0, 0);
    float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;

    // 内旋涡丝: 角向按 1/r 扭曲(越近拽得越弯), 整体缓旋
    float twist = uStrength * 0.55 / (r + 0.18);
    float swirl = tex2D(uImage0, float2(ang * 2 + twist + uTime * 0.13, r * 1.6 - uTime * 0.55)).r;
    float streaks = pow(swirl, 3.2) * saturate(r * 1.8) * uStrength;

    // 压缩环纹: 向心行进的细环(空间被一圈圈压进去)
    float ripple = pow(0.5 + 0.5 * sin(r * 26 - uTime * 7.5), 6) * saturate(r * 2.2) * 0.32 * uStrength;

    // 吸积亮缘: r~0.8 处一圈受噪声抖动的亮环
    float jitter = tex2D(uImage0, float2(ang * 4 + uTime * 0.3, 0.35)).g;
    float rim = exp(-pow((r - (0.78 + jitter * 0.08)) * 9, 2)) * 0.65 * uStrength;

    float3 col = float3(0.60, 0.30, 1.00) * (streaks * 1.25 + ripple)
               + float3(0.86, 0.62, 1.00) * rim;

    // 中心域压暗: 光被吞入(NonPremultiplied 下 alpha 压底色)
    float darkA = (1 - smoothstep(0.05, 0.60, r)) * 0.5 * uStrength;

    float lum = saturate(streaks + ripple + rim);
    float alpha = saturate(darkA + lum * 0.85) * saturate((1 - r) * 5) * uOpacity;
    return float4(col, alpha);
}

technique Technique1
{
    pass WellPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
