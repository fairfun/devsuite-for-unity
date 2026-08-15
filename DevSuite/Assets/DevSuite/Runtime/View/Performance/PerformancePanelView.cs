using Ff.DevSuite.Performance;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View
{
    internal class PerformancePanelView : VisualElement
    {
        private VisualElement _graphsContainer;

        private readonly List<(BaseGraphDataProvider data, PerformanceGraphView view)> _graphs = new();

        private DevSuiteContext _context;

        public PerformancePanelView(VisualTreeAsset uxml, StyleSheet uss)
        {
            uxml.CloneTree(this);
            styleSheets.Add(uss);

            var root = this.Q<VisualElement>("panel-root") ?? this;
            root.AddToClassList("ff-panel");
            _graphsContainer = root.Q<VisualElement>("graphs-container");
        }

        public void Initialize(DevSuiteContext context)
        {
            _context = context;
            UpdateViews();
        }

        private void Subscribe()
        {
            Unsubscribe();
            _context.OnPerformancePanelChanged += HandleContextChanged;
        }

        private void Unsubscribe()
        {
            if (_context != null)
            {
                _context.OnPerformancePanelChanged -= HandleContextChanged;
            }
        }

        private void HandleContextChanged()
        {
            UpdateViews();
        }

        private void UpdateViews()
        {
            Subscribe();

            bool structureMatch = _graphs.Count == _context.PerformancePanelProviders.Count;
            if (structureMatch)
            {
                for (int i = 0; i < _graphs.Count; i++)
                {
                    if (_graphs[i].data != _context.PerformancePanelProviders[i])
                    {
                        structureMatch = false;
                        break;
                    }
                }
            }

            if (structureMatch)
            {
                foreach (var graph in _graphs)
                {
                    graph.view.UpdateViewState();
                }
                return;
            }

            Reset();
            foreach (var provider in _context.PerformancePanelProviders)
            {
                RegisterGraph(provider);
            }

            if (_graphsContainer.childCount > 0)
            {
                _graphsContainer[_graphsContainer.childCount - 1].AddToClassList("graph-view--last");
            }
        }

        private void RegisterGraph(BaseGraphDataProvider dataProvider)
        {
            var graphView = new PerformanceGraphView(dataProvider, _context);
            _graphsContainer.Add(graphView);
            _graphs.Add((dataProvider, graphView));
        }

        public void Reset()
        {
            Unsubscribe();
            foreach (var graph in _graphs)
            {
                graph.view.Reset();
            }
            _graphsContainer?.Clear();
            _graphs.Clear();
        }
    }
}