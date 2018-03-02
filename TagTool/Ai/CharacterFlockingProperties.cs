using TagTool.Common;
using TagTool.Serialization;

namespace TagTool.Ai
{
    [TagStructure(Size = 0x18)]
    public class CharacterFlockingProperties
    {
        public float DecelerationDistance;
        public float NormalizedSpeed;
        public float BufferDistance;
        public Bounds<float> ThrottleThresholdBOunds;
        public float DecelerationStopTime;
    }
}