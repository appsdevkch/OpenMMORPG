using UnityEngine;
using UnityEngine.AI;

namespace MultiplayerARPG
{
    /// <summary>
    /// Root motion for an entity that moves on the navmesh - every monster in the demo.
    ///
    /// The kit's <see cref="NavMeshEntityMovement"/> has no root motion at all: an attack or skill
    /// animation with <c>shouldUseRootMotion</c> ticked plays on the spot. (Its character
    /// controller movement does have it, on the entity; a player's Animator sits there for that
    /// reason - see <see cref="ModelAnimator"/>.) This does the same job from the demo's side, so
    /// the kit's movement code is left as upstream wrote it.
    ///
    /// While such an animation plays, the entity is moved by the animation instead of by its
    /// agent:
    /// - **The agent is stopped first.** A monster that has arrived somewhere still has that spot
    ///   as its destination, and an agent that is not stopped walks straight back to it - the
    ///   lunge was undone within half a second when this was first tried (2026-11-16).
    /// - **It is moved through the agent**, which keeps it on the navmesh, and the transform is
    ///   then moved as far as the agent went, because a stopped agent no longer writes its
    ///   position there (the kit turns <c>updatePosition</c> off in <c>StopMove</c>).
    ///
    /// Only where the movement is simulated - the server, for a monster. Everyone else follows
    /// the synced position. A server needs <c>GameInstance.updateAnimationAtServer</c> on (it is,
    /// in the demo), or it plays no animation to take the motion from.
    ///
    /// Belongs beside the <see cref="Animator"/>: Unity sends <c>OnAnimatorMove</c> to that object
    /// alone. Having this there also means the Animator hands its root motion to a script rather
    /// than applying it, which changes nothing on screen: with root motion not in use it was
    /// dropped before and is dropped now, and no bone moves differently (checked bone by bone on
    /// the bandit, wolf and deer rigs). Added to every monster by <c>DemoEntityBuilder</c> and
    /// <c>DemoWildlifeBuilder</c>; <c>Open MMORPG > Demo > Refresh Root Motion</c> puts it on the
    /// ones already built.
    /// </summary>
    [DisallowMultipleComponent]
    public class NavMeshRootMotion : MonoBehaviour
    {
        private Animator _animator;
        private BaseGameEntity _entity;
        private NavMeshEntityMovement _movement;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _entity = GetComponentInParent<BaseGameEntity>();
            _movement = _entity != null ? _entity.GetComponent<NavMeshEntityMovement>() : null;
        }

        private void OnAnimatorMove()
        {
            if (_animator == null || _movement == null || !_movement.enabled)
                return;
            if (!_entity.ShouldUseRootMotion || !_movement.CanPredictMovement())
                return;
            NavMeshAgent agent = _movement.CacheNavMeshAgent;
            if (agent == null || !agent.isOnNavMesh)
                return;

            if (!agent.isStopped)
                _movement.StopMove();

            Vector3 motion = _animator.deltaPosition;
            if (motion.sqrMagnitude <= 0f)
                return;
            Vector3 before = agent.nextPosition;
            agent.Move(motion);
            // By how far the agent went, not to where it is: a stopped agent has usually drifted a
            // few centimetres from the transform while it slowed down (the kit closes that gap
            // when the monster next walks), and going to it here popped the body at the first
            // frame of the lunge.
            if (!agent.updatePosition)
                _entity.EntityTransform.position += agent.nextPosition - before;
        }
    }
}
