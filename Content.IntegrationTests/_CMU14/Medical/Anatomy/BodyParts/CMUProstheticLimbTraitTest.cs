using System.Collections.Generic;
using System.Linq;
using Content.Shared.Administration.Systems;
using Content.Shared.CMU14.Medical.Anatomy.BodyParts;
using Content.Shared.CMU14.Medical.Core;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.CMU14.Medical.Anatomy.BodyParts;

[TestFixture]
public sealed class CMUProstheticLimbTraitTest
{
    [Test]
    public async Task RoundStartProstheticTraitsCreateCompleteLimbs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;
        var biologicalParts = new Dictionary<(BodyPartType, BodyPartSymmetry), EntityUid>();

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);
            var medical = entMan.System<CMUMedicalBodyIndexSystem>();
            foreach (var type in new[] { BodyPartType.Arm, BodyPartType.Hand, BodyPartType.Leg, BodyPartType.Foot })
            {
                foreach (var symmetry in new[] { BodyPartSymmetry.Left, BodyPartSymmetry.Right })
                {
                    Assert.That(medical.TryGetBodyPart(
                        human,
                        new CMUMedicalBodyPartKey(type, symmetry),
                        out var part),
                        Is.True,
                        $"missing initial {symmetry} {type}");
                    biologicalParts.Add((type, symmetry), part);
                }
            }

            var initialHands = entMan.GetComponent<HandsComponent>(human);
            Assert.Multiple(() =>
            {
                Assert.That(initialHands.Hands.Keys, Is.EquivalentTo(new[] { "left", "right" }));
                Assert.That(initialHands.SortedHands, Is.EqualTo(new[] { "right", "left" }));
                Assert.That(initialHands.ActiveHandId, Is.Not.Null);
            });

            entMan.EnsureComponent<CMUProstheticLeftArmComponent>(human);
            entMan.EnsureComponent<CMUProstheticRightArmComponent>(human);
            entMan.EnsureComponent<CMUProstheticLeftLegComponent>(human);
            entMan.EnsureComponent<CMUProstheticRightLegComponent>(human);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var handsSystem = entMan.System<SharedHandsSystem>();
            var medical = entMan.System<CMUMedicalBodyIndexSystem>();

            var leftArm = AssertRoboticPart(entMan, medical, human, BodyPartType.Arm, BodyPartSymmetry.Left);
            var leftHand = AssertRoboticPart(entMan, medical, human, BodyPartType.Hand, BodyPartSymmetry.Left);
            var rightArm = AssertRoboticPart(entMan, medical, human, BodyPartType.Arm, BodyPartSymmetry.Right);
            var rightHand = AssertRoboticPart(entMan, medical, human, BodyPartType.Hand, BodyPartSymmetry.Right);
            var leftLeg = AssertRoboticPart(entMan, medical, human, BodyPartType.Leg, BodyPartSymmetry.Left);
            var leftFoot = AssertRoboticPart(entMan, medical, human, BodyPartType.Foot, BodyPartSymmetry.Left);
            var rightLeg = AssertRoboticPart(entMan, medical, human, BodyPartType.Leg, BodyPartSymmetry.Right);
            var rightFoot = AssertRoboticPart(entMan, medical, human, BodyPartType.Foot, BodyPartSymmetry.Right);

            var hands = entMan.GetComponent<HandsComponent>(human);
            var body = entMan.GetComponent<BodyComponent>(human);
            Assert.Multiple(() =>
            {
                Assert.That(biologicalParts.Values.All(part => !entMan.EntityExists(part)), Is.True,
                    "each replaced biological root/extremity subtree must be deleted");
                Assert.That(entMan.GetComponent<ChildOrganComponent>(leftHand).Parent, Is.EqualTo(leftArm));
                Assert.That(entMan.GetComponent<ChildOrganComponent>(rightHand).Parent, Is.EqualTo(rightArm));
                Assert.That(entMan.GetComponent<ChildOrganComponent>(leftFoot).Parent, Is.EqualTo(leftLeg));
                Assert.That(entMan.GetComponent<ChildOrganComponent>(rightFoot).Parent, Is.EqualTo(rightLeg));
                Assert.That(body.Organs!.ContainedEntities, Has.Count.EqualTo(17));
                Assert.That(body.Organs.ContainedEntities.Distinct().Count(), Is.EqualTo(17));
                Assert.That(hands.Hands.Keys, Is.EquivalentTo(new[] { "left", "right" }));
                Assert.That(hands.SortedHands, Is.EqualTo(new[] { "right", "left" }));
                Assert.That(hands.ActiveHandId, Is.Not.Null);
                Assert.That(
                    handsSystem.TryGetHand(
                        (human, hands),
                        "left",
                        out _),
                    Is.True);
                Assert.That(
                    handsSystem.TryGetHand(
                        (human, hands),
                        "right",
                        out _),
                    Is.True);
                Assert.That(
                    handsSystem.TryGetHand(
                        (human, hands),
                        SharedBodySystem.GetPartSlotContainerId("left_hand"),
                        out _),
                    Is.False);
                Assert.That(
                    handsSystem.TryGetHand(
                        (human, hands),
                        SharedBodySystem.GetPartSlotContainerId("right_hand"),
                        out _),
                    Is.False);
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(human));
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RejuvenateRestoresProstheticLegAndMovement(bool keepFoot)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entMan = pair.Server.EntMan;
        EntityUid human = default;
        EntityUid oldFoot = default;
        EntityUid restoredFoot = default;
        EntityUid? carrier = null;
        float healthySprint = 0;

        try
        {
            await pair.Server.WaitPost(() =>
            {
                human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);
                entMan.EnsureComponent<CMUProstheticLeftLegComponent>(human);
            });
            await pair.RunTicksSync(5);

            await pair.Server.WaitAssertion(() =>
            {
                var medical = entMan.System<CMUMedicalBodyIndexSystem>();
                var leg = AssertRoboticPart(entMan, medical, human, BodyPartType.Leg, BodyPartSymmetry.Left);
                oldFoot = AssertRoboticPart(entMan, medical, human, BodyPartType.Foot, BodyPartSymmetry.Left);
                healthySprint = entMan.GetComponent<MovementSpeedModifierComponent>(human).CurrentSprintSpeed;
                Assert.That(healthySprint, Is.Positive);

                if (keepFoot)
                    entMan.System<OrganRelationSystem>().Orphan(oldFoot);

                carrier = entMan.System<DetachableOrganSystem>().Detach(leg);
                Assert.That(carrier, Is.Not.Null);
                Assert.That(entMan.System<CMULegSupportSystem>().HasLegsToStand(human), Is.False);
            });
            await pair.RunTicksSync(5);

            await pair.Server.WaitAssertion(() =>
            {
                entMan.System<RejuvenateSystem>().PerformRejuvenate(human);

                var medical = entMan.System<CMUMedicalBodyIndexSystem>();
                var leg = AssertRoboticPart(entMan, medical, human, BodyPartType.Leg, BodyPartSymmetry.Left);
                restoredFoot = AssertRoboticPart(entMan, medical, human, BodyPartType.Foot, BodyPartSymmetry.Left);
                Assert.That(entMan.GetComponent<ChildOrganComponent>(restoredFoot).Parent, Is.EqualTo(leg));
                Assert.That(restoredFoot == oldFoot, Is.EqualTo(keepFoot));
                Assert.That(entMan.System<CMULegSupportSystem>().HasLegsToStand(human), Is.True);
            });
            await pair.RunTicksSync(5);

            await pair.Server.WaitAssertion(() =>
            {
                var medical = entMan.System<CMUMedicalBodyIndexSystem>();
                Assert.That(AssertRoboticPart(entMan, medical, human, BodyPartType.Foot, BodyPartSymmetry.Left),
                    Is.EqualTo(restoredFoot));
                Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(human).CurrentSprintSpeed,
                    Is.EqualTo(healthySprint).Within(0.01f));
            });
        }
        finally
        {
            await pair.Server.WaitPost(() =>
            {
                if (carrier is { } detachedBody)
                    entMan.DeleteEntity(detachedBody);
                entMan.DeleteEntity(human);
            });
        }

        await pair.CleanReturnAsync();
    }

    private static EntityUid AssertRoboticPart(
        IEntityManager entMan,
        CMUMedicalBodyIndexSystem medical,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        Assert.That(
            medical.TryGetBodyPart(body, new CMUMedicalBodyPartKey(type, symmetry), out var part),
            Is.True,
            $"missing {symmetry} {type}");
        Assert.That(entMan.HasComponent<CMURoboticLimbComponent>(part), Is.True, $"non-robotic {symmetry} {type}");
        return part;
    }
}
