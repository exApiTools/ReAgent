using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;

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

    [Api]
    public int CurrentAnimationId
    {
        get
        {
            if (_entity.TryGetComponent<Actor>(out var actor) && actor.AnimationController != null)
            {
                return actor.AnimationController.CurrentAnimationId;
            }
            return -1;
        }
    }

    [Api]
    public int CurrentAnimationStage
    {
        get
        {
            if (_entity.TryGetComponent<Actor>(out var actor) && actor.AnimationController != null)
            {
                return actor.AnimationController.CurrentAnimationStage;
            }
            return -1;
        }
    }

    [Api]
    public float AnimationProgress
    {
        get
        {
            if (_entity.TryGetComponent<Actor>(out var actor) && actor.AnimationController != null)
            {
                return actor.AnimationController.AnimationProgress;
            }
            return 0f;
        }
    }
}
