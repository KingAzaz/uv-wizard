#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UVWizard.Core.Packing;
using UVWizard.Core.Pipeline;
using UVWizard.Core.Reporting;
using UVWizard.Core.Tests;
using Object = UnityEngine.Object;

namespace UVWizard.NDMF.Tests
{
    // The report window (#99): repainting a published report, with every section filled in, raises no exception or
    // layout error, including after the build's objects are gone. What each section shows isn't checked.
    public class BakeReportWindowTests : ObjectFixture
    {
        BakeReportWindow? window;
        bool wasOpen;
        BakeReport? userReport;

        static readonly Dictionary<Object, Object> NoOriginals = new Dictionary<Object, Object>();

        [SetUp]
        public void SetUp()
        {
            if (Application.isBatchMode)
                Assert.Ignore("A batch-mode editor has no window views, so the report window never repaints.");
            wasOpen = EditorWindow.HasOpenInstances<BakeReportWindow>();
            userReport = BakeReports.Latest;
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null && !wasOpen) window.Close();
            if (userReport != null) BakeReports.Publish(userReport, NoOriginals);
            else BakeReports.Clear();
        }

        [Test]
        public void AReportWithNoFiguresDraws()
        {
            BakeReports.Publish(new BakeReport("Avatar", mobilePreview: false, null, null), NoOriginals);

            Draw();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AReportWhoseMeshesNothingMergedDraws()
        {
            var figures = new ReportFigures(3, 2, 1, 4, PerformanceRank.Medium, PerformanceRank.Medium);
            BakeReports.Publish(new BakeReport("Avatar", mobilePreview: false, null, PerformanceRank.Good, figures, figures,
                meshesMerged: false), NoOriginals);

            Draw();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AReportDrawsAfterTheBuildDestroyedItsRenderersAndMaterials()
        {
            var body = Renderer("Body");
            var hat = Renderer("Hat");
            var swapped = Material("Swapped");
            var glass = Material("Glass");
            var result = new BakeResult(true, Material("Merged"), null, null, Array.Empty<(Mesh, Mesh)>(),
                new[] { new SlotNote(body, 0, swapped, new Reason(ReasonText.CutoutDropped)) },
                new[] { new SlotNote(hat, 0, glass, new Reason(ReasonText.TransparencyDropped)) },
                packing: new PackingOutcome(Packer.Exact, 1, true));
            var figures = new ReportFigures(4, 2, 1, 12.5f, PerformanceRank.Poor, PerformanceRank.Poor);
            BakeReports.Publish(new BakeReport("Avatar", mobilePreview: true, result, PerformanceRank.Good, figures, figures,
                new[] { new LeftoverRenderer(hat, LeftoverReason.Toggle) }), NoOriginals);

            Object.DestroyImmediate(body.gameObject);
            Object.DestroyImmediate(hat.gameObject);
            Object.DestroyImmediate(swapped);
            Object.DestroyImmediate(glass);
            Draw();

            LogAssert.NoUnexpectedReceived();
        }

        // EditorWindow.RepaintImmediately is internal; it runs the layout and repaint passes of OnGUI at once.
        void Draw()
        {
            window = EditorWindow.GetWindow<BakeReportWindow>();
            typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);
        }

        Renderer Renderer(string name)
        {
            var renderer = new GameObject(name).AddComponent<MeshRenderer>();
            Keep(renderer.gameObject);
            return renderer;
        }

        Material Material(string name)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            Keep(material);
            return material;
        }
    }
}
