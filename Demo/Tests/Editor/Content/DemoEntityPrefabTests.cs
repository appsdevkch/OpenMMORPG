using System.Collections.Generic;
using MultiplayerARPG;
using NUnit.Framework;
using OpenMMORPG.Tests;

namespace OpenMMORPG.Demo.Tests
{
    /// <summary>
    /// Fields on the kit's entity templates that silently win over the data the demo sets up
    /// (2026-09-15). After cloning a template, these are the ones to clear or fill.
    /// </summary>
    [Category(TestCategories.Content)]
    public class DemoEntityPrefabTests
    {
        /// <summary>
        /// A plain entity animates only through its serialized `model`, which the kit fills with
        /// GetComponent, not GetComponentInChildren. The model sits on a child, so every NPC stood
        /// frozen and the patrolling guard slid along with still legs.
        /// </summary>
        [Test]
        public void EveryNpcWithAModelHasItWired()
        {
            List<string> broken = new List<string>();
            foreach ((NpcEntity npc, string path) in DemoTestPaths.Prefabs<NpcEntity>())
            {
                if (npc.GetComponentInChildren<GameEntityModel>(true) != null &&
                    TestReflection.GetField<GameEntityModel>(npc, "model") == null)
                    broken.Add(path);
            }
            Assert.That(broken, Is.Empty, "NPCs that will never animate");
        }

        /// <summary>
        /// `entityTitle` overrides the monster's data title, and the kit's template ships it as
        /// "Enemy", so every bandit, marauder and cultist wore the same nameplate.
        /// </summary>
        [Test]
        public void MonstersShowTheirOwnName()
        {
            List<string> broken = new List<string>();
            foreach ((BaseMonsterCharacterEntity monster, string path) in DemoTestPaths.Prefabs<BaseMonsterCharacterEntity>())
            {
                string title = TestReflection.GetField<string>(monster, "entityTitle");
                if (!string.IsNullOrEmpty(title))
                    broken.Add($"{path}: \"{title}\"");
            }
            Assert.That(broken, Is.Empty, "monster prefabs whose entityTitle hides their MonsterCharacter title");
        }

        /// <summary>
        /// The kit applies a player's root motion from `OnAnimatorMove` on the entity's movement
        /// component, and Unity sends that only to the Animator's own object. With the Animator
        /// on the model child, root motion did nothing at all (2026-11-16). The entity builder
        /// puts it on the entity; a rebuild that left it on the model would bring that back.
        /// </summary>
        [Test]
        public void APlayersAnimatorIsOnItsEntity()
        {
            List<string> broken = new List<string>();
            foreach ((BasePlayerCharacterEntity player, string path) in DemoTestPaths.Prefabs<BasePlayerCharacterEntity>())
            {
                if (player.GetComponent<CharacterControllerEntityMovement>() == null)
                    continue;
                UnityEngine.Animator animator = player.GetComponent<UnityEngine.Animator>();
                CharacterModelManager manager = player.GetComponent<CharacterModelManager>();
                IModelWithAnimator model = manager != null ? manager.MainTpsModel as IModelWithAnimator : null;
                if (animator == null)
                    broken.Add($"{path}: no Animator on the entity");
                else if (model == null || model.Animator != animator)
                    broken.Add($"{path}: its main model is not animated by the entity's Animator");
            }
            Assert.That(broken, Is.Empty, "player prefabs whose root motion will never run");
        }

        /// <summary>
        /// The kit's navmesh movement has no root motion, so a monster's attack with
        /// `shouldUseRootMotion` ticked played on the spot. The demo's `NavMeshRootMotion` does it,
        /// and has to sit beside the Animator, which is where Unity sends `OnAnimatorMove`.
        /// </summary>
        [Test]
        public void EveryNavMeshMonsterHasRootMotionBesideItsAnimator()
        {
            List<string> broken = new List<string>();
            foreach ((BaseMonsterCharacterEntity monster, string path) in DemoTestPaths.Prefabs<BaseMonsterCharacterEntity>())
            {
                if (monster.GetComponent<NavMeshEntityMovement>() == null)
                    continue;
                UnityEngine.Animator animator = monster.GetComponentInChildren<UnityEngine.Animator>(true);
                if (animator != null && animator.GetComponent<NavMeshRootMotion>() == null)
                    broken.Add(path);
            }
            Assert.That(broken, Is.Empty, "monster prefabs whose root motion animations will play on the spot");
        }
    }
}
