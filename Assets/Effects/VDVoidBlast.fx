//方形图的 coords 从 0 到 1,中心在 0.5,r 按半边长归一,可见内容不超过 0.96,输出预乘 alpha 并用 AlphaBlend 混合(暗核要压暗背景),没有动态分支
//s1 是 GradientNoise(实测 0.09 到 0.93)给团块和边缘起伏,s2 是 SwirlNoise(0 到 0.88,均值 0.2)给壳里的等离子丝
//团块和丝都用直角坐标随壳等比外扩,边缘起伏用单位方向向量取样,不用 atan2,没有极坐标接缝
sampler uImage0 : register(s0);
sampler uImage1 : register(s1);
sampler uImage2 : register(s2);

float uProgress;//该值是寿命进度,0 到 1
float uTime;//该值只驱动壳内的流动
float uSeed;//该值让同屏几团爆炸的花纹错开
float uOpacity;
float uDarkness;//该值是暗核不透明度上限,0 到 1,电弧爆炸给低值
float uShell;//该值是等离子壳外缘的最大半径,0.5 到 0.8
float3 uColorHot;//该值是点火和内缘的热色
float3 uColorMain;//该值是等离子壳主色
float3 uColorCool;//该值是冷却后的外缘色
float3 uColorDark;//该值是虚空暗核色

float4 PixelFunc(float4 baseColor : COLOR0, float2 coords : TEXCOORD0) : COLOR0
{
    float t = saturate(uProgress);
    float2 p = (coords - 0.5) * 2.0;
    float r = length(p);
    float2 dir = p / max(r, 0.0001);
    float2 seedOff = float2(uSeed * 0.37, uSeed * 0.61);

    //三条半径曲线:壳外缘缓出、暗核在 14% 之后张开追向外缘、冲击环跑在最前
    float eo = 1.0 - pow(1.0 - t, 3.0);
    float ro = lerp(0.12, uShell, eo);
    float coreOpen = smoothstep(0.14, 1.0, t);
    float ri = lerp(-0.12, uShell * 0.9, pow(coreOpen, 0.85));
    float rs = lerp(0.14, 0.93, 1.0 - pow(1.0 - t, 4.0));

    //材质坐标随壳等比外扩,花纹贴在物质上一起飞散
    float2 m = p / max(eo, 0.3);
    float2 drift = float2(uTime * 0.02, -uTime * 0.015);
    float w1 = tex2D(uImage1, frac(m * 0.45 + seedOff + 0.13)).r;
    float w2 = tex2D(uImage1, frac(m * 0.45 + seedOff + 0.71)).r;
    float2 mw = m + (float2(w1, w2) - 0.5) * 0.5;
    float clump = tex2D(uImage1, frac(mw * 0.7 + seedOff + drift)).r * 0.65
        + tex2D(uImage1, frac(mw * 1.6 + seedOff * 1.3 + 0.37 - drift)).r * 0.35;
    float fil = tex2D(uImage2, frac(mw * 0.55 + seedOff * 0.7 + drift * 2.0)).r;
    float edgeN = tex2D(uImage1, frac(dir * 0.75 + seedOff + 0.53)).r;
    float ringN = tex2D(uImage1, frac(dir * 1.6 + seedOff * 1.7 + 0.29)).r;

    //角向起伏在圆心附近收掉,否则暗核刚张开时会在中心挤出一颗星
    float rr = r + (edgeN - 0.5) * (0.06 + 0.12 * t) * smoothstep(0.0, 0.25, r) + (clump - 0.5) * 0.1;

    //等离子壳:径向轮廓乘团块,内缘被暗核啃,外缘按团块撕开
    float profile = smoothstep(ri - 0.02, ri + 0.12, rr) * (1.0 - smoothstep(ro - 0.2, ro + 0.04, rr));
    float body = profile * smoothstep(0.22, 0.7, clump + 0.22 * (1.0 - t));
    float s = saturate((rr - ri) / max(ro - ri, 0.05));
    float cool = saturate(t * 1.05 + s * 0.45 - fil * 0.3);
    float3 shellCol = lerp(uColorHot, uColorMain, smoothstep(0.05, 0.5, cool));
    shellCol = lerp(shellCol, uColorCool, smoothstep(0.5, 1.0, cool));
    float bright = body * (1.45 - t * 1.1) * (0.55 + 0.9 * clump + 1.6 * fil);

    //点火:前 14% 的白热团,随即被壳吃掉
    float flash = (1.0 - smoothstep(0.0, 0.14, t)) * (1.0 - smoothstep(0.0, 0.34 + 0.35 * t, r));

    //暗核与贴着它的热缘;暗核在 58% 之后先于壳淡出,边缘软
    float coreLife = smoothstep(0.1, 0.26, t) * (1.0 - smoothstep(0.58, 0.95, t));
    float dark = (1.0 - smoothstep(ri - 0.14, ri + 0.03, rr)) * coreLife * uDarkness;
    float rimD = (rr - ri) / 0.035;
    float rim = exp(-rimD * rimD) * smoothstep(0.04, 0.16, ri) * (1.0 - t) * (0.4 + 0.6 * clump + 0.8 * fil);

    //冲击环:细、快,亮度沿角向起伏但不断开
    float ringD = (r - rs) / lerp(0.012, 0.03, t);
    float ring = exp(-ringD * ringD) * pow(1.0 - t, 1.6) * (0.4 + 0.6 * ringN);

    float guard = 1.0 - smoothstep(0.9, 0.99, r);

    float3 emis = shellCol * bright + uColorHot * rim * 1.2 + float3(1.0, 1.0, 1.0) * flash * 1.2
        + lerp(uColorHot, uColorMain, 0.6) * ring * 0.9;
    float emisA = saturate(bright * 0.45 + rim * 0.3 + flash * 0.7 + ring * 0.22);
    float3 col = emis + uColorDark * dark * (1.0 - emisA);
    float a = saturate(emisA + dark * (1.0 - emisA));

    float k = uOpacity * guard;
    return float4(col * k, a * k) * baseColor;
}

technique Technique1
{
    pass BlastPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
