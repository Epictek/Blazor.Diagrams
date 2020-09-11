﻿using Microsoft.AspNetCore.Components;
using Microsoft.Msagl.Core;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Incremental;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Layout.MDS;
using Microsoft.Msagl.Miscellaneous;
using System;
using System.Collections.Generic;
using System.Linq;
using MNode = Microsoft.Msagl.Core.Layout.Node;

namespace Excubo.Blazor.Diagrams
{
    public enum Algorithm
    {
        Ranking,
        FastIncremental,
        MultiDimensionalScaling,
        Sugiyama,
        TreeVertical,
        TreeHorizontal
    }
    public class AutoLayoutSettings : ComponentBase
    {
        [CascadingParameter] public Diagram Diagram { get; set; }
        protected override void OnParametersSet()
        {
            System.Diagnostics.Debug.Assert(Diagram != null);
            Diagram.AutoLayoutSettings = this;
            base.OnParametersSet();
        }
        private Algorithm algorithm;
        [Parameter]
        public Algorithm Algorithm
        {
            get => algorithm;
            set
            {
                if (value == algorithm)
                {
                    return;
                }
                algorithm = value;
                Run();
            }
        }
        public void Run()
        {
            if (Diagram != null && Diagram.Nodes != null && Diagram.Links != null)
            {
                Layout(Diagram.Nodes.all_nodes, Diagram.Links.all_links);
            }
        }
        internal void Layout(List<NodeBase> all_nodes, List<LinkBase> all_links)
        {
            switch (Algorithm)
            {
                case Algorithm.TreeVertical:
                case Algorithm.TreeHorizontal:
                    TreeAlgorithm(all_nodes, all_links);
                    break;
                case Algorithm.Sugiyama:
                case Algorithm.Ranking:
                case Algorithm.MultiDimensionalScaling:
                case Algorithm.FastIncremental:
                    MSAGL(all_nodes, all_links);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        #region tree
        private void TreeAlgorithm(List<NodeBase> all_nodes, List<LinkBase> all_links)
        {
            // 1. Establish hierarchy.
            var layers = GetLayers(all_nodes, all_links);
            // 2. Now that everything is in layers, we should arrange the items per layer such that there is minimal crossing of links.
            ArrangeNodesWithinLayers(all_links, layers);
            // 3. Layout time!
            ArrangeNodes(all_links, layers);
            // 4. fix link positions. This is easy, because we work top down, except when we have upwards arrows (cycles) or ltr/rtl arrows.
            ArrangeLinks(all_links, layers);
        }
        private void ArrangeLinks(List<LinkBase> all_links, List<List<NodeBase>> layers)
        {
            if (Algorithm == Algorithm.TreeVertical)
            {
                ArrangeLinksForVerticalTree(all_links, layers);
            }
            else
            {
                ArrangeLinksForHorizontalTree(all_links, layers);
            }
        }
        private static void ArrangeLinksForHorizontalTree(List<LinkBase> all_links, List<List<NodeBase>> layers)
        {
            foreach (var link in all_links)
            {
                if (link.Source != null && link.Target != null)
                {
                    var source_layer_index = layers.IndexOf(layers.First(l => l.Contains(link.Source.Node)));
                    var target_layer_index = layers.IndexOf(layers.First(l => l.Contains(link.Target.Node)));
                    if (source_layer_index < target_layer_index)
                    {
                        link.Source.Port = Position.Right;
                        link.Target.Port = Position.Left;
                    }
                    else if (source_layer_index > target_layer_index)
                    {
                        link.Source.Port = Position.Left;
                        link.Target.Port = Position.Right;
                    }
                    else
                    {
                        var ltr = link.Source.Node.Y < link.Target.Node.Y;
                        link.Source.Port = ltr ? Position.Bottom : Position.Top;
                        link.Target.Port = ltr ? Position.Top : Position.Bottom;
                    }
                }
            }
        }
        private static void ArrangeLinksForVerticalTree(List<LinkBase> all_links, List<List<NodeBase>> layers)
        {
            foreach (var link in all_links)
            {
                if (link.Source != null && link.Target != null)
                {
                    var source_layer_index = layers.IndexOf(layers.First(l => l.Contains(link.Source.Node)));
                    var target_layer_index = layers.IndexOf(layers.First(l => l.Contains(link.Target.Node)));
                    if (source_layer_index < target_layer_index)
                    {
                        link.Source.Port = Position.Bottom;
                        link.Target.Port = Position.Top;
                    }
                    else if (source_layer_index > target_layer_index)
                    {
                        link.Source.Port = Position.Top;
                        link.Target.Port = Position.Bottom;
                    }
                    else
                    {
                        var ltr = link.Source.Node.X < link.Target.Node.X;
                        link.Source.Port = ltr ? Position.Right : Position.Left;
                        link.Target.Port = ltr ? Position.Left : Position.Right;
                    }
                }
            }
        }
        private void ArrangeNodes(List<LinkBase> all_links, List<List<NodeBase>> layers)
        {
            const double vertical_separation = 50;
            const double horizontal_separation = 50;
            if (Algorithm == Algorithm.TreeVertical)
            {
                ArrangeNodesInRows(layers, all_links, vertical_separation, horizontal_separation);
            }
            else
            {
                ArrangeNodesInColumns(layers, all_links, vertical_separation, horizontal_separation);
            }
        }
        private static void ArrangeNodesInColumns(List<List<NodeBase>> layers, List<LinkBase> all_links, double vertical_separation, double horizontal_separation)
        {
            double x = 0;
            var heights = layers.Select(layer =>
                layer.Select(n => n.Height + n.GetDrawingMargins().Top + n.GetDrawingMargins().Bottom).ToList())
                .ToList();
            var layer_heights = heights.Select(layer => horizontal_separation * (layer.Count - 1) + layer.Sum()).ToList();
            var highest_layer_height = layer_heights.Max();
            foreach (var (layer, height) in layers.Zip(layer_heights, (l, h) => (l, h)))
            {
                double y = (highest_layer_height - height) / 2;
                foreach (var node in layer)
                {
                    node.MoveTo(x, y);
                    var margins = node.GetDrawingMargins();
                    y += node.Height + margins.Top + margins.Bottom + vertical_separation;
                }
                var maximum_width = layer.Max(n => n.Width + n.GetDrawingMargins().Left + n.GetDrawingMargins().Right);
                x += horizontal_separation + maximum_width;
            }
        }
        private static void ArrangeNodesInRows(List<List<NodeBase>> layers, List<LinkBase> all_links, double vertical_separation, double horizontal_separation)
        {
            // Preliminary placement of items in first layer
            double y = 0;
            {
                var layer = layers.First();
                double x = 0;
                foreach (var node in layer)
                {
                    node.MoveTo(x, y);
                    var margins = node.GetDrawingMargins();
                    x += node.Width + margins.Left + margins.Right + horizontal_separation;
                }
                var maximum_height = layer.Max(n => n.Height + n.GetDrawingMargins().Top + n.GetDrawingMargins().Bottom);
                y += vertical_separation + maximum_height;
            }
            // All subsequent layers: set position to average position of parents
            for (int i = 0; i + 1 < layers.Count; ++i)
            {
                var top_layer = layers[i];
                var layer = layers[i + 1];
                var desired_positions = layer.Select(node =>
                {
                    var connected_nodes = all_links.Where(l => l.Target?.Node == node && top_layer.Contains(l.Source?.Node)).Select(l => l.Source?.Node).ToList();
                    return connected_nodes.Any() ? connected_nodes.Average(n => n.X) : 0;
                }).ToList();
                // sort nodes by desired position
                layers[i + 1] = layer.Zip(desired_positions, (n, dp) => (n, dp)).OrderBy(kv => kv.dp).Select(kv => kv.n).ToList();
                desired_positions = desired_positions.OrderBy(v => v).ToList();
                layer = layers[i + 1];
                // ensure there's enough separation between all nodes. let's find the center first
                var center = desired_positions.Average();
                for (int j = 0; j + 1 < layer.Count; ++j)
                {
                    desired_positions[j + 1] = Math.Max(desired_positions[j + 1], desired_positions[j] + horizontal_separation + layer[j].Width + layer[j].GetDrawingMargins().Left + layer[j].GetDrawingMargins().Right);
                }
                // find how much off-center we are now
                var new_center = desired_positions.Average();
                for (int j = 0; j < layer.Count; ++j)
                {
                    layer[j].MoveTo(desired_positions[j] - (new_center - center) / 2, y);
                }
                var maximum_height = layer.Max(n => n.Height + n.GetDrawingMargins().Top + n.GetDrawingMargins().Bottom);
                y += vertical_separation + maximum_height;
            }
            // now we can fix the first layer
            if (layers.Count > 1)
            {
                var desired_positions = layers[0].Select(node =>
                {
                    var connected_nodes = all_links.Where(l => l.Source?.Node == node && layers[1].Contains(l.Target?.Node)).Select(l => l.Target?.Node).ToList();
                    return connected_nodes.Any() ? connected_nodes.Average(n => n.X) : 0;
                }).ToList();
                // sort nodes by desired position
                layers[0] = layers[0].Zip(desired_positions, (n, dp) => (n, dp)).OrderBy(kv => kv.dp).Select(kv => kv.n).ToList();
                desired_positions = desired_positions.OrderBy(v => v).ToList();
                // ensure there's enough separation between all nodes. let's find the center first
                var center = desired_positions.Average();
                for (int j = 0; j + 1 < layers[0].Count; ++j)
                {
                    desired_positions[j + 1] = Math.Max(desired_positions[j + 1], desired_positions[j] + horizontal_separation + layers[0][j].Width + layers[0][j].GetDrawingMargins().Left + layers[0][j].GetDrawingMargins().Right);
                }
                // find how much off-center we are now
                var new_center = desired_positions.Average();
                for (int j = 0; j < layers[0].Count; ++j)
                {
                    layers[0][j].MoveTo(desired_positions[j] - (new_center - center) / 2, 0);
                }
            }

        }
        private static void ArrangeNodesWithinLayers(List<LinkBase> all_links, List<List<NodeBase>> layers)
        {
            // 2.1. sort out each layer by looking at where it connects from
            for (var i = 0; i + 1 < layers.Count; ++i)
            {
                var top_layer = layers[i];
                layers[i + 1] = layers[i + 1].OrderBy(node =>
                {
                    // calculate average position based on connected nodes in top layer
                    var connected_nodes = all_links.Where(l => l.Target?.Node == node && top_layer.Contains(l.Source?.Node)).Select(l => l.Source?.Node).ToList();
                    if (!connected_nodes.Any())
                    {
                        return 0;
                    }
                    var average_index = connected_nodes.Select(cn => { var i = top_layer.IndexOf(cn); return i; }).Average();
                    return average_index;
                }).ToList();
            }
            // 2.2. now that all but the first layer are layed out, let's deal with the first
            if (layers.Count > 1)
            {
                layers[0] = layers[0].OrderBy(node =>
                {
                    // calculate average position based on connected nodes in top layer
                    var connected_nodes = all_links.Where(l => l.Source?.Node == node && layers[1].Contains(l.Target?.Node)).Select(l => l.Target?.Node).ToList();
                    if (!connected_nodes.Any())
                    {
                        return 0;
                    }
                    var average_index = connected_nodes.Select(cn => { var i = layers[1].IndexOf(cn); return i; }).Average();
                    return average_index;
                }).ToList();
            }
        }
        private static List<List<NodeBase>> GetLayers(List<NodeBase> all_nodes, List<LinkBase> all_links)
        {
            // 1.1. We identify the nodes that have no incoming link
            var roots = all_nodes.Where(n => !all_links.Any(l => l.Target?.Node == n)).ToList();
            // 1.2. If we do not have any such node, we exclusively have cycles. We now randomly pick a root. That random choice is the first node. Chosen by a fair die, as legend has it.
            roots = roots.Any() ? roots : all_nodes.Take(1).ToList();
            // 1.3. This makes our first layer.
            var layers = new List<List<NodeBase>> { roots };
            // 1.4. Now we identify all nodes that have an incoming link from this node and put those into the second layer.
            // We continue this until all nodes have been assigned to layers.
            var remaining_nodes = all_nodes.Except(roots).ToList();
            while (remaining_nodes.Any())
            {
                // 1.4.1. we look at the last layer
                var last_layer = layers.Last();
                // 1.4.2. find all targets that nodes in the last layer point to
                var targets_of_last_layer = all_links.Where(l => last_layer.Contains(l.Source?.Node)).Select(l => l.Target?.Node).ToList();
                // 1.4.3. but only look at the ones that aren't assigned to layers yet
                var targets_of_last_layer_among_remaining = targets_of_last_layer.Intersect(remaining_nodes).ToList();
                if (!targets_of_last_layer_among_remaining.Any())
                {
                    // we have a problem: the last layer doesn't point to anything, but we haven't taken care of all nodes yet. That surely doesn't happen. does it?
                    // Backup plan until we "know": simply create a dump layer.
                    layers.Add(remaining_nodes);
                    break;
                }
                // 1.4.4. create the new layer
                layers.Add(targets_of_last_layer_among_remaining);
                // 1.4.5. update the remaining node list
                remaining_nodes = remaining_nodes.Except(targets_of_last_layer_among_remaining).ToList();
            }

            return layers;
        }
        #endregion
        #region msagl
        private void MSAGL(List<NodeBase> all_nodes, List<LinkBase> all_links)
        {

            var graph = new GeometryGraph();
            foreach (var node in all_nodes)
            {
                var n = new MNode(CurveFactory.CreateRectangle(new Rectangle(node.X, node.Y, node.X + node.Width, node.Y + node.Height)));
                graph.Nodes.Add(n);
            }
            foreach (var link in all_links)
            {
                if (link.Source.Node != null && link.Target.Node != null)
                {
                    var e = new Edge(graph.Nodes[all_nodes.IndexOf(link.Source.Node)], graph.Nodes[all_nodes.IndexOf(link.Target.Node)]);
                    graph.Edges.Add(e);
                }
            }
            LayoutAlgorithmSettings settings = Algorithm switch
            {
                Algorithm.Ranking => new Microsoft.Msagl.Prototype.Ranking.RankingLayoutSettings(),
                Algorithm.FastIncremental => new FastIncrementalLayoutSettings(),
                Algorithm.MultiDimensionalScaling => new MdsLayoutSettings(),
                Algorithm.Sugiyama => new SugiyamaLayoutSettings(),
                _ => new SugiyamaLayoutSettings()
            };
            settings.Reporting = false;
            LayoutHelpers.CalculateLayout(graph, settings, new CancelToken());
            foreach (var (node, gnode) in all_nodes.Zip(graph.Nodes, (a, b) => (a, b)))
            {
                node.MoveTo(gnode.Center.X - gnode.Width / 2 - graph.Left, gnode.Center.Y - gnode.Height / 2 - graph.Bottom);
            }
            foreach (var (link, glink) in all_links.Zip(graph.Edges, (a, b) => (a, b)))
            {
                if (link.Source.Node != null && link.Target.Node != null)
                {
                    var raw_angle = Math.Atan2(glink.Target.Center.Y - glink.Source.Center.Y, glink.Target.Center.X - glink.Source.Center.X) * 180 / Math.PI + 450;
                    var iangle = (int)Math.Floor(raw_angle) % 360;
                    var angle = iangle / 45;
                    link.Source.Port = ToPort(angle);
                    link.Target.Port = ToPort((angle + 4) % 8);
                    link.TriggerStateHasChanged();
                }
            }
        }
        private static Position ToPort(int angle)
        {
            return angle switch
            {
                0 => Position.Top,
                1 => Position.TopRight,
                2 => Position.Right,
                3 => Position.BottomRight,
                4 => Position.Bottom,
                5 => Position.BottomLeft,
                6 => Position.Left,
                7 => Position.TopLeft,
                _ => Position.Top
            };
        }
        #endregion
    }
}
