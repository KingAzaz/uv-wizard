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
using Object = UnityEngine.Object;

namespace UVWizard.NDMF.Tests
{
    // The report window (#99): it draws every section of a published report without an exception or a layout error,
    // including after the build's objects are gone.
    public class BakeReportWindowTests
    {
        readonly List<Object> created = new List<Object>();
        BakeReportWindow? window;

        [TearDown]
        public void TearDown()
        {
            if (window != null) window.Close();
            BakeReports.Clear();
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void AReportWithNoFiguresDraws()
        {
            BakeReports.Publish(new BakeReport("Avatar", mobilePreview: false, null, null));

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
                new[] { new LeftoverRenderer(hat, LeftoverReason.Toggle) }));

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
            created.Add(renderer.gameObject);
            return renderer;
        }

        Material Material(string name)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            created.Add(material);
            return material;
        }
    }
}
