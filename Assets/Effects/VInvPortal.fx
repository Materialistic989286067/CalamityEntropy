// VInvPortal — 虚空入侵传送门(裂隙)着色器
// 用法: CEFxcEffects.Get("VInvPortal"), SpriteBatch Immediate + NonPremultiplied + LinearWrap,
// 画一张任意噪声图(s0)撑满门面矩形; uv 在片元里换极坐标自算, 贴图内容只当噪声源用。
// 结构: 双层反向视差涡流(门内深渊) + 中心黑洞压暗 + 噪声锯齿的边缘撕裂光。
sampler uImage0 : register(s0);

float uTime;      // 秒级时间(GlobalTimeWrappedHourly)
float uOpacity;   // 总透明度(开合包络)
float uBoost;     // 亮度增幅(开门瞬间 >1 过曝, 常态 1)
float3 uColorDeep; // 深渊底色(近黑紫)
float3 uColorMid;  // 涡流中间色(紫)
float3 uColorRim;  // 边缘撕裂光色(亮紫粉)

float4 PixelFunc(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = uv * 2 - 1;
    float r = length(p);
    if (r > 1)
        return float4(0, 0, 0, 0);
    float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;

    // 双层涡流: 角向随半径扭曲(内圈拖拽更狠), 两层反向异速滚动制造视差纵深
    float2 swirlUV1 = float2(ang * 2 + r * 1.35 - uTime * 0.21, r * 1.15 - uTime * 0.62);
    float2 swirlUV2 = float2(-ang * 3 + r * 0.75 + uTime * 0.09, r * 2.4 - uTime * 0.33);
    float n1 = tex2D(uImage0, swirlUV1).r;
    float n2 = tex2D(uImage0, swirlUV2).g;
    float swirl = n1 * 0.62 + n2 * 0.38;

    // 门内深渊: 中心暗、外圈亮的径向明度, 涡流纹理调出亮丝
    float depth = pow(r, 1.55);
    float3 col = lerp(uColorDeep, uColorMid, saturate(depth * (0.3 + swirl * 0.9)));
    col += uColorMid * pow(swirl, 3) * depth * 1.4;

    // 边缘撕裂光: 环带半径被低频噪声抖动出锯齿, 高光强度再吃一层噪声闪烁
    float tear = tex2D(uImage0, float2(ang * 3 + uTime * 0.45, uTime * 0.14)).r;
    float rimBand = saturate(1 - abs(r - (0.84 + tear * 0.12)) * 11);
    col += uColorRim * pow(rimBand, 2) * (0.7 + 0.6 * tear);

    // 中心黑洞压暗(吞噬感): r<~0.35 迅速压向纯黑
    col *= saturate(r * 2.6 - 0.06);

    // 盘面实心, 最外缘羽化
    float alpha = saturate((1 - r) * 7) * uOpacity;
    return float4(col * uBoost, alpha);
}

technique Technique1
{
    pass PortalPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
