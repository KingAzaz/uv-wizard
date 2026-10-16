#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UVWizard.Core.Budgets;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;
using Object = UnityEngine.Object;

namespace UVWizard.Core.Tests.Reporting
{
    // The end-of-build figures against the preset's budget (#83).
    public class BudgetCheckTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void MoreSlotsThanTheBudgetAllowsAreOver()
        {
            var check = BudgetCheck.Slots(2, Budget.Good, Array.Empty<LeftoverRenderer>());

            Assert.That(check.Outcome, Is.EqualTo(SlotOutcome.Over));
            Assert.That(check.MmdBody, Is.Null);
        }

        [Test]
        public void SlotsWithinTheBudgetAreWithin()
        {
            Assert.That(BudgetCheck.Slots(2, Budget.Medium, Array.Empty<LeftoverRenderer>()).Outcome, Is.EqualTo(SlotOutcome.Within));
        }

        [Test]
        public void WhenTheOnlyExtraSlotIsTheBodyKeptForMmdWorldsItNamesTheBody()
        {
            // The merged mesh and the Body share the merged material, and the SDK counts a slot on each.
            var body = new LeftoverRenderer(Skinned("Body", slots: 1), LeftoverReason.MmdBody);

            var check = BudgetCheck.Slots(2, Budget.Good, new[] { body });

            Assert.That(check.Outcome, Is.EqualTo(SlotOutcome.OverOnlyForMmdBody));
            Assert.That(check.MmdBody, Is.EqualTo(body));
        }

        [Test]
        public void ABodyOfTwoSlotsKeptForMmdWorldsAccountsForBoth()
        {
            var body = new LeftoverRenderer(Skinned("Body", slots: 2), LeftoverReason.MmdBody);

            Assert.That(BudgetCheck.Slots(3, Budget.Good, new[] { body }).Outcome, Is.EqualTo(SlotOutcome.OverOnlyForMmdBody));
        }

        [Test]
        public void SlotsOverTheBudgetBesidesTheMmdBodyAreOver()
        {
            // An excluded material's slot is over Good's one besides the Body's.
            var body = new LeftoverRenderer(Skinned("Body", slots: 1), LeftoverReason.MmdBody);

            var check = BudgetCheck.Slots(3, Budget.Good, new[] { body });

            Assert.That(check.Outcome, Is.EqualTo(SlotOutcome.Over));
            Assert.That(check.MmdBody, Is.Null);
        }

        [TestCase(LeftoverReason.Toggle)]
        [TestCase(LeftoverReason.StaticMesh)]
        [TestCase(LeftoverReason.BoundsOrRootBone)]
        [TestCase(LeftoverReason.Other)]
        public void AMeshKeptApartForAnotherReasonIsOver(LeftoverReason reason)
        {
            var apart = new LeftoverRenderer(Skinned("Body", slots: 1), reason);

            Assert.That(BudgetCheck.Slots(2, Budget.Good, new[] { apart }).Outcome, Is.EqualTo(SlotOutcome.Over));
        }

        [Test]
        public void TextureMemoryOverTheBudgetWithNoAtlasIsOver()
        {
            Assert.That(BudgetCheck.Texture(10.5f, Budget.Excellent, Baked(merged: false, Budget.Excellent, sizedFor: null)),
                Is.EqualTo(TextureOutcome.Over));
        }

        [Test]
        public void TextureMemoryOverTheBudgetWithTheAtlasSizedForALowerRankSaysSo()
        {
            Assert.That(BudgetCheck.Texture(17, Budget.Excellent, Baked(merged: true, Budget.Excellent, sizedFor: Budget.Good)),
                Is.EqualTo(TextureOutcome.OverLowerRank));
        }

        [Test]
        public void TextureMemoryOverTheBudgetWithTheAtlasAtItsFinestSizesIsOverEveryRank()
        {
            Assert.That(BudgetCheck.Texture(26, Budget.Excellent, Baked(merged: true, Budget.Excellent, sizedFor: null)),
                Is.EqualTo(TextureOutcome.OverEveryRank));
        }

        [Test]
        public void TextureMemoryOverTheBudgetTheAtlasWasSizedForIsOver()
        {
            Assert.That(BudgetCheck.Texture(10.5f, Budget.Excellent, Baked(merged: true, Budget.Excellent, sizedFor: Budget.Excellent)),
                Is.EqualTo(TextureOutcome.Over));
        }

        [Test]
        public void TextureMemoryAtTheBudgetIsWithin()
        {
            Assert.That(BudgetCheck.Texture(10, Budget.Excellent, Baked(merged: true, Budget.Excellent, sizedFor: Budget.Excellent)),
                Is.EqualTo(TextureOutcome.Within));
        }

        // A bake aiming for the budget, which merged slots into one material or not, with its atlas sized for sizedFor.
        BakeResult Baked(bool merged, Budget budget, Budget? sizedFor) =>
            new BakeResult(true, merged ? Keep(new Material(Shader.Find("Standard"))) : null, null, null,
                    Array.Empty<(Mesh, Mesh)>(), Array.Empty<SlotNote>(), Array.Empty<SlotNote>())
                .Measured(Array.Empty<Renderer>(), budget, sizedFor);

        // A skinned renderer with the given number of material slots.
        SkinnedMeshRenderer Skinned(string name, int slots)
        {
            var renderer = Keep(new GameObject(name)).AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterials = new Material[slots];
            return renderer;
        }

        T Keep<T>(T o) where T : Object
        {
            created.Add(o);
            return o;
        }
    }
}
