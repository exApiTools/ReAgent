using ExileCore.PoEMemory.Components;

namespace ReAgent.State;

[Api]
public class AnimationControllerInfo
{
    [Api]
    public int CurrentAnimationId { get; }

    [Api]
    public int CurrentAnimationStage { get; }

    [Api]
    public float AnimationProgress { get; }

    public AnimationControllerInfo(AnimationController animationController)
    {
        if (animationController == null)
            return;

        CurrentAnimationId = animationController.CurrentAnimationId;
        CurrentAnimationStage = animationController.CurrentAnimationStage;
        AnimationProgress = animationController.AnimationProgress;
    }
}
