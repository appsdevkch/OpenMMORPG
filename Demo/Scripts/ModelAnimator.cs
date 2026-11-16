using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Finds the <see cref="Animator"/> that animates a character model.
    ///
    /// Usually it is on the model's own object. A player's is on the **entity** instead
    /// (<c>DemoEntityBuilder.PutAnimatorOnEntity</c>): Unity sends <c>OnAnimatorMove</c> only to
    /// the Animator's own object, and the kit's root motion lives on the entity's movement
    /// component, so with the Animator on the model the kit's root motion never ran. The model
    /// component still names its Animator, so that is asked before the parents are searched -
    /// an entity can hold more than one body (the rider's seated copy keeps its own Animator).
    /// </summary>
    public static class ModelAnimator
    {
        public static Animator Of(Component model)
        {
            if (model == null)
                return null;
            Animator animator = model.GetComponent<Animator>();
            if (animator != null)
                return animator;
            IModelWithAnimator withAnimator = model.GetComponent<IModelWithAnimator>();
            if (withAnimator != null && withAnimator.Animator != null)
                return withAnimator.Animator;
            return model.GetComponentInParent<Animator>(true);
        }
    }
}
