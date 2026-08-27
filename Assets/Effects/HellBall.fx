// CalamityEntropy:HellBall
// 行为等效替代灾厄同名旧键的自有实现: 圆形能量护盾(纵向滚动噪声)
// 调用姿势: 裸 Effect 挂 SpriteBatch Immediate, 参数名逐个 SetValue, 必须与下列声明一字不差
// (shieldEdgeBlendStrenght 的拼写错误来自原调用契约, 故意保留)
float time;
float blowUpPower;   // 噪声图球面化的幂次
float blowUpSize;    // 球面化挤出强度
float3 shieldColor;
float shieldOpacity;
float3 shieldEdgeColor;
float shieldEdgeBlendStrenght;
float noiseScale;
float resolution;

// 采样的是 SpriteBatch 当前绘制贴图(寄存器 s0), wrap 寻址保证滚动无缝
texture sampleTexture;
sampler2D NoiseSampler = sampler_state { texture = <sampleTexture>; magfilter = LINEAR; minfilter = LINEAR; mipfilter = LINEAR; AddressU = wrap; AddressV = wrap; };

float4 PixelFunc(float2 uv : TEXCOORD) : COLOR
{
    // 圆形裁剪
    float distFromCenter = length(uv - float2(0.5, 0.5)) * 2;
    if (distFromCenter > 1)
        return float4(0, 0, 0, 0);

    // 把平面噪声 uv 朝边缘挤出, 伪装成球面
    float bulgeX = pow(abs(uv.x - 0.5) * 2, blowUpPower);
    float bulgeY = pow(abs(uv.y - 0.5) * 2, blowUpPower);
    float2 sphereUV = float2(
        uv.x * (1 + bulgeY * blowUpSize) - bulgeY * blowUpSize * 0.5,
        uv.y * (1 + bulgeX * blowUpSize) - bulgeX * blowUpSize * 0.5);

    // 缩放后沿纵轴滚动
    sphereUV *= noiseScale;
    sphereUV.y = (sphereUV.y + time) % 1;

    float4 noiseColor = tex2D(NoiseSampler, sphereUV);

    // 伪菲涅尔: 边缘增亮
    noiseColor += pow(distFromCenter, 6);

    // 最外 5% 渐隐
    if (distFromCenter > 0.95)
        noiseColor *= 1 - (distFromCenter - 0.95) / 0.05;

    // 主色到边缘色按距离幂次混合
    return noiseColor * float4(lerp(shieldColor, shieldEdgeColor, pow(distFromCenter, shieldEdgeBlendStrenght)), shieldOpacity);
}

technique Technique1
{
    pass HellBallPass
    {
        PixelShader = compile ps_2_0 PixelFunc();
    }
}
