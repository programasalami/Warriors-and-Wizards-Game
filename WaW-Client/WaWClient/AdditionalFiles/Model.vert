#version 330 core

uniform mat4 FullMatrix;
// The camera's depth column (M12, M22, M32, M42 of the camera matrix, as the CPU's DepthMatrix): screen y of a ground point =
// x * M12 + y * M22 + M42. Used for baked static props (see below). FullMatrix is uploaded transposed, so it is NOT re-derived from it.
uniform vec4 DepthColumn;

layout (location = 0) in vec3 Position;
layout (location = 1) in vec2 BaseUV;
layout (location = 2) in vec3 iPosition;
layout (location = 3) in vec4 iUV;
layout (location = 4) in vec3 iExtra;

out MODEL_OUT {
    vec2 BaseUV;
    vec4 UV;
    vec3 Extra;
    float Zed;
} output1;

void main() {
    float s = sin(iExtra.x);
    float c = cos(iExtra.x);

    vec4 pos = vec4(Position.xy * mat2(c, -s, s, c), Position.z, 1);
    pos = vec4((pos.xy - 0.5) * 1.005 + 0.5, pos.zw);
    pos.xyz += iPosition;

    output1.BaseUV = BaseUV;
    output1.UV = iUV;
    output1.Extra = iExtra;
    output1.Zed = pos.z;

    pos = pos * FullMatrix;

    // Depth. A number >= 0 is the depth itself (worked out on the CPU from the camera, every frame). Static props baked once into a
    // per-area mesh (StaticProps.cs, 2026-09-22) cannot know the camera, so they carry a code instead and the depth is computed here with
    // the same formula Entity.UpdateVisibility uses: 0.5 + 0.4 x the screen y of the ground point + a tiny per-object nudge.
    //   -10 + nudge x 1000: the ground point is iPosition.xy (props, card corners)
    //   -20 + nudge x 1000: the ground point is iPosition.xy + 0.5 (walls and wall tops, stored at their tile corner)
    float depth = iExtra.y;
    if (depth < -5.0) {
        float center = depth < -15.0 ? -20.0 : -10.0;
        vec2 ground = iPosition.xy + (depth < -15.0 ? vec2(0.5, 0.5) : vec2(0.0, 0.0));
        depth = 0.5 + 0.4 * (ground.x * DepthColumn.x + ground.y * DepthColumn.y + DepthColumn.w) + (depth - center) * 0.001;
    }
    pos.z = depth;

    gl_Position = pos;
}
