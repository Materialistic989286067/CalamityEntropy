// CruiserSkyFilter — 巡游者虚空扭曲全屏滤镜(Filters.Scene["CalamityEntropy:Cruiser"])
// 取代旧 CrSky.Draw 内的 RenderTarget 扭曲流程(fscreenCr + drawLine 遮罩):
// 噪声驱动 UV 位移(竖向带状衰减)+ 亮度卷曲。
// 强度 uOpacity = CruiserSkyDrive.Intensity(UseOpacity)× Filter 淡入(UseGlobalOpacity),原版自动相乘。
// 用法: 注册于 EntropySkies.setUpShaderFilters,CrScreenShaderData 每帧喂
// uScreenOffCE(镜头视差)/uCoordMultCE(1/缩放)/uBandCenterCE(扭曲带中心,重力翻转时翻到下侧)。
sampler uImage0 : register(s0);   // 捕获的画面
sampler uImage1 : register(s1);   // 噪声(VoidBack,UseImage 绑 s1)

float uTime;          // 原版自动喂: Main.GlobalTimeWrappedHourly
float uOpacity;       // 原版自动喂: 见上
float2 uScreenOffCE;  // 镜头视差偏移(噪声采样)
float2 uCoordMultCE;  // 噪声坐标缩放补偿(1/GameViewMatrix.Zoom)
float uBandCenterCE;  // 扭曲带中心(屏高比例)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float strengthMult = 0.28 * uOpacity;
    // 竖向软衰减带,替代旧 RT drawLine 遮罩(3000px 宽线在 1080p 本就近乎全屏软罩)
    float dy = (uv.y - uBandCenterCE) * 1.6;
    float band = exp2(-dy * dy);
    float strength = strengthMult * band;

    float t = uTime * 0.03;
    float n1 = tex2D(uImage1, frac(uv * uCoordMultCE + float2(t + 0.4, t + 0.7) + uScreenOffCE)).r;
    float n2 = tex2D(uImage1, frac(uv * uCoordMultCE + float2(-t + 0.3, -t + 0.5) + uScreenOffCE)).r;
    float2 offset = float2(n1 - 0.5, n2 - 0.5) * strength * 0.2;

    float4 color = tex2D(uImage0, uv + offset);
    // 亮度卷曲(沿用旧 fscreenCr 常数 cNum=1.12, cStrength=3.2)
    float cc = (color.r + color.g + color.b - 1.12) * 3.2;
    color.rgb *= 1 + cc * strengthMult;
    return color;
}

technique Technique1
{
    pass CruiserSkyPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
