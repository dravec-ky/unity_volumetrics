#include "HLSLSupport.cginc"
//cginc raymarch function

//https://www.shadertoy.com/view/3sffzj
//https://github.com/SebLague/Clouds/blob/master/Assets/Scripts/Clouds/Shaders/CloudSky.shader
//invRaydir = 1/raydir

#pragma shader_feature_local _SHAPE_TEXTURE
#pragma shader_feature_local _WORLD_SCALE_NOISE
#pragma shader_feature_local _CALCULATE_LIGHT
#pragma shader_feature_local _FIXED_RAY_SIZE
#pragma shader_feature_local _RAY_NOISE
#pragma shader_feature_local _CHEAP_NOISE_READ
#pragma shader_feature_local _LIGHTING_QUALITY_LOW _LIGHTING_QUALITY_MEDIUM _LIGHTING_QUALITY_HIGH _LIGHTING_QUALITY_VERY_HIGH
#pragma shader_feature_local _TRANSMITTANCE_CLAMP
    
#define STEPS_PRIMARY 16
#define STEPS_SECONDARY 8

#if defined(_LIGHTING_QUALITY_LOW)
    #define STEPS_LIGHT 1
#elif  defined(_LIGHTING_QUALITY_MEDIUM)
    #define STEPS_LIGHT 2
#elif defined(_LIGHTING_QUALITY_HIGH)
    #define STEPS_LIGHT 4
#elif defined(_LIGHTING_QUALITY_VERY_HIGH)
    #define STEPS_LIGHT 6
#else
    #define STEPS_LIGHT 6
#endif

#ifdef _CHEAP_NOISE_READ
    float _NoiseReadThreshold = 0.0;
#endif

#ifdef _TRANSMITTANCE_CLAMP
    float _MinTransmittance  = 0.0001;
#endif

struct shapeInfo
{
    float radius;
    float bottom;
    float top;
};

struct noiseInfo
{
    float4 strength;
    float4 speed;
    float scale;
    float exp;
};


    // --------------------------------------------------------------------

float2 rayBoxDst(float3 boundsMin, float3 boundsMax, float3 rayOrigin, float3 invRaydir)
{
    // Adapted from: http://jcgt.org/published/0007/03/04/
    float3 t0 = (boundsMin - rayOrigin) * invRaydir;
    float3 t1 = (boundsMax - rayOrigin) * invRaydir;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
    
    float dstA = max(max(tmin.x, tmin.y), tmin.z);
    float dstB = min(tmax.x, min(tmax.y, tmax.z));

    float dstToBox = max(0, dstA);
    float dstInsideBox = max(0, dstB - dstToBox);
    return float2(dstA, dstInsideBox);
}

    // --------------------------------------------------------------------

float HenyeyGreenstein(float g, float costh){
	return (1.0 / 2.0)  * ((1.0 - g * g) / pow(1.0 + g*g - 2.0*g*costh, 1.5));
}

    // --------------------------------------------------------------------

float remap(float x, float low1, float high1, float low2, float high2){
	return saturate(low2 + (x - low1) * (high2 - low2) / (high1 - low1));
}

    // --------------------------------------------------------------------

// https://twitter.com/FewesW/status/1364629939568451587/photo/1
float3 multipleOctaves(float extinction, float mu, float stepL){

    float3 sigmaE = float3(1,1,1);
    float3 luminance = float3(0,0,0);
    const int octaves = 4;
    
    // Attenuation
    float a = 0.0;
    // Contribution
    float b = 1.0;
    // Phase attenuation
    float c = 0.0;
    
    float phase;

    [loop]
    for(int i = 0; i < octaves; i++){
        // Two-lobed HG
        phase = lerp(HenyeyGreenstein(-0.1 * c, mu), HenyeyGreenstein(0.3 * c, mu), 0.7);
        luminance += b * phase * exp(-stepL * extinction * sigmaE * a);
        // Lower is brighter
        a *= 0.2;
        // Higher is brighter
        b *= 0.5;
        c *= 0.5;
    }
    return luminance;
}

    // --------------------------------------------------------------------

float getNoise(sampler3D shape, float3 pos, noiseInfo n, float3 wind)
{
#ifdef _WORLD_SCALE_NOISE
    float4 world_pos = mul(unity_ObjectToWorld, float4(pos,0))*0.01;
    pos = world_pos.xyz;
#endif
    float4 noise = pow(1-tex3D(shape, pos*n.scale + wind*_Time),n.exp)*n.strength;
    float max_noise = noise.x+noise.y+noise.z+noise.w;
    return max_noise;
}

    // --------------------------------------------------------------------

float getDensity(sampler3D shape, float3 pos, shapeInfo s)
{
#ifdef _SHAPE_TEXTURE
    return tex3D(shape, pos.xzy).r;
#else
    float3 distVec = pos - float3(0.5,0.5,0.5);
    float distSq = dot(distVec,distVec);
    float maxVal = max(max(distVec.x*distVec.x, distVec.z*distVec.z), distVec.y*distVec.y);

    float bottom = (saturate(-distVec.y) * s.bottom);
    float top = (saturate(distVec.y) * s.top);

    float density = saturate((0.25*s.radius-distSq)*4 + bottom*bottom + top );
    return density * saturate((1-maxVal*4)*4);
#endif
}

    // --------------------------------------------------------------------

float4 RaymarchX(float3 rayOrigin, float3 rayDirection, float maxDist,
                    //SUN & COLOURING
                    float3 sunDirection, float3 sunLightColour, float4 scatterColour, float4 ambientColour, 
                    //SHAPE
                    sampler3D shapeVolume, shapeInfo shapeVars,
                    //NOISE
                    sampler3D noiseVolume, float4 noiseStrength, float4 noiseSpeed, float noiseScale, float noiseExp,
                    //DENSITY
                    float densityScale, float densityThreshold, 
                    //SHADING
                    float sunPower, float3 lightAbsorb, float lightStepSize,
                    float powderAmount,  float lightLining, float phase,
                    
                    //SPEED
                    float3 windDir, 
                    out float debugInfo)
{
    
    float TotalTransmittance = 1.0f;
    

    float3 FinalColour = float3(0,0,0);

    
    float3 bounds = float3(0.5,0.5,0.5);

    float2 dists = rayBoxDst(bounds*-1, bounds, rayOrigin, 1/rayDirection);
    if(dists.x>maxDist)
        return float4(FinalColour, TotalTransmittance);

    noiseInfo noiseVars;
    noiseVars.strength = noiseStrength;
    noiseVars.speed = noiseSpeed;
    noiseVars.scale = noiseScale;
    noiseVars.exp = noiseExp;

    // Used for effect which differ based on the angle of the sun and view vectors
    float mu = dot(sunDirection, rayDirection)*0.5 + 0.5;

    // Scattering and absorption coefficients
    float3 sigmaS = scatterColour.xyz;
    float3 sigmaA = float3(1e-6,1e-6,1e-6);

    // Extinction coefficient.
    float3 sigmaE = float3(1,1,1);

    dists.x = max(dists.x,0.0);

#ifdef _RAY_NOISE
    float rayNoiseScale = 128/(STEPS_PRIMARY*STEPS_SECONDARY);
    float rayNoise = tex3D(noiseVolume, rayDirection*1000).w*rayNoiseScale;
    dists += float2( (sign(dists[0]))*-rayNoise*0.5 , rayNoise*0.5);

    float3 maxPos = float3(1+rayNoiseScale,1+rayNoiseScale,1+rayNoiseScale);
    float3 minPos = float3(-rayNoiseScale,-rayNoiseScale,-rayNoiseScale);
#else
    float3 maxPos = float3(1,1,1);
    float3 minPos = float3(0,0,0);
#endif
    
#ifdef _FIXED_RAY_SIZE
    // Approx. maximum possible distance inside a 1 by 1 cube
    float rayDist = 1.55;
#else
    float rayDist = min(maxDist-dists[0], dists[1]);
#endif
    
    float stepSize = rayDist/STEPS_PRIMARY;  
    float smallStepSize = (stepSize)/STEPS_SECONDARY;

    rayOrigin += rayDirection*(dists[0]+smallStepSize);
    lightStepSize = lightStepSize*0.01;
    //All calculations are performed in <0,1> UV space instead of <-.5,.5> local space
    rayOrigin += float3(.5,.5,.5);

    //https://omlc.org/classroom/ece532/class3/hg.html
    float phaseFunction = lerp(HenyeyGreenstein(-0.3, mu*mu*mu), HenyeyGreenstein(phase, mu*mu*mu), mu*mu*mu*lightLining);

    float3 sunLight = sunLightColour * sunPower;

    //bool value to make the detailed loop run again on the next iteration
    float repeatLoop = 0.0f;

    float noise_sum = (noiseVars.strength.x + noiseVars.strength.y + noiseVars.strength.z + noiseVars.strength.w);

    [loop]
    for(uint i=1; i < STEPS_PRIMARY; i++)
    {
        float density = getDensity(shapeVolume, (rayOrigin + rayDirection*stepSize), shapeVars) + repeatLoop;

        if(density > densityThreshold)
        {
            float3 smallRayOrigin = rayOrigin - rayDirection*stepSize;
            [loop]
            for(uint j=1; j < STEPS_SECONDARY; j++)
            {
                smallRayOrigin += rayDirection*smallStepSize;
                density = getDensity(shapeVolume, smallRayOrigin, shapeVars);
                float4 noise = getNoise(noiseVolume, smallRayOrigin, noiseVars, windDir);
                float noise_density = remap(density, noise, 1.0+noise_sum, 0.0, 1.0);

                if(noise_density>densityThreshold)
                {
                    float3 sampleSigmaS = sigmaS * noise_density * densityScale;
                    float3 sampleSigmaE = sigmaE * noise_density * densityScale;
                
                #ifdef _CALCULATE_LIGHT
                    //LIGHT RAY CALCULATION
                    float3 lightRayOrigin = smallRayOrigin;
                    float lightRayDensity = 0.0f;
                    [unroll(STEPS_LIGHT)]
                    for(int k=0;k<STEPS_LIGHT;k++)
                    {
                        lightRayOrigin += sunDirection*lightStepSize;
                        density = getDensity(shapeVolume, lightRayOrigin, shapeVars);

                    #ifdef _CHEAP_NOISE_READ
                        if(dot(TotalTransmittance, TotalTransmittance) < _NoiseReadThreshold) {
                    #endif
                            noise = getNoise(noiseVolume, lightRayOrigin, noiseVars, windDir);
                            noise_density = remap(density, noise, 1.0+noise_sum, 0.0, 1.0);
                    #ifdef _CHEAP_NOISE_READ
                        } else { noise_density = density; }
                    #endif
                        lightRayDensity += noise_density;
                    }
                    lightRayDensity = (lightRayDensity/STEPS_LIGHT)*lightAbsorb * densityScale;
                    
                    // Return product of Beer's law and powder effect depending on the 
                    // view direction angle with the light direction.
                    float3 trans_exp = exp(-lightStepSize * lightRayDensity * sigmaE);
                    float3 powder = 1-trans_exp;
                    float3 beersLaw = trans_exp;
                    //float3 beersLaw = multipleOctaves(lightRayDensity, mu, lightStepSize);

                    float3 lightRay = lerp(powder * beersLaw  * 5 , beersLaw, (1-powderAmount) + powderAmount*(mu));
                    float3 luminance = ambientColour + sunLight * lightRay * phaseFunction;
                    // Scale light contribution by density of the cloud.
                    luminance *= sampleSigmaS;

                    // Beer-Lambert.
                    float3 transmittance = exp(-sampleSigmaE * smallStepSize);

                    // Better energy conserving integration
                    // "From Physically based sky, atmosphere and cloud rendering in Frostbite" 5.6
                    // by Sebastian Hillaire.
                    FinalColour += TotalTransmittance *  (luminance - luminance * transmittance) / sampleSigmaE; 

                    
                #else
                    float3 transmittance = exp(-sampleSigmaE * smallStepSize);
                    FinalColour += TotalTransmittance *  (1 - 1*transmittance*transmittance*transmittance) / sampleSigmaE; 
                #endif
                    TotalTransmittance *= transmittance;
                    
                #ifdef _TRANSMITTANCE_CLAMP
                    if(dot(TotalTransmittance, TotalTransmittance) <= _MinTransmittance){
                        TotalTransmittance = float3(0.0,0.0,0.0);
                    #ifdef _CALCULATE_LIGHT
                        FinalColour += _MinTransmittance*luminance/ sampleSigmaE;
                    #else
                        FinalColour += _MinTransmittance / sampleSigmaE;
                    #endif
                        j = STEPS_SECONDARY;
                        i = STEPS_PRIMARY;
                    }
                #endif
                }
            }
            repeatLoop = 1.0f;
        }
        else
        {
            repeatLoop = 0.0f;
        }
            rayOrigin += rayDirection*stepSize;

        #ifdef _FIXED_RAY_SIZE
            if((any(rayOrigin>maxPos) || any(rayOrigin<minPos)))
                i = STEPS_PRIMARY;
        #endif
    }
    debugInfo = rayDist;
#ifndef _CALCULATE_LIGHT
    FinalColour = lerp(ambientColour, scatterColour, (saturate(pow(1-FinalColour.r,3)))*scatterColour.a);
#endif
    return float4(FinalColour, TotalTransmittance);
}