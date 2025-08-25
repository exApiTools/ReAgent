using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;

namespace ReAgent.State;

[Api]
public class ActorInfo
{
    private readonly Entity _entity;

    public ActorInfo(Entity entity)
    {
        _entity = entity;
    }

    [Api]
    public string Action
    {
        get
        {
            if (_entity.TryGetComponent<Actor>(out var actor))
            {
                return actor.Action.ToString();
            }

            return null;
        }
    }

    [Api]
    public string Animation
    {
        get
        {
            if (_entity.TryGetComponent<Actor>(out var actor))
            {
                return actor.Animation.ToString();
            }

            return null;
        }
    }

    private bool TryGetAnimationController(out AnimationController animationController)
    {
        if (_entity.TryGetComponent<Actor>(out var actor) &&
            actor.AnimationController is { } ac ||
            _entity.TryGetComponent<Animated>(out var animated) &&
            animated.BaseAnimatedObjectEntity is { } baseEntity &&
            baseEntity.TryGetComponent(out ac))
        {
            animationController = ac;
            return true;
        }

        animationController = null;
        return false;
    }

    [Api]
    public int CurrentAnimationId
    {
        get
        {
            if (TryGetAnimationController(out var ac))
            {
                return ac.CurrentAnimationId;
            }

            return -1;
        }
    }

    [Api]
    public int CurrentAnimationStage
    {
        get
        {
            if (TryGetAnimationController(out var ac))
            {
                return ac.CurrentAnimationStage;
            }

            return -1;
        }
    }

    [Api]
    public float AnimationProgress
    {
        get
        {
            if (TryGetAnimationController(out var ac))
            {
                return ac.AnimationProgress;
            }

            return 0f;
        }
    }
}