using System;
using System.Collections.Generic;

namespace Ff.DevSuite
{
    internal class DisposeAction : IDisposable
    {
        private Action _singleAction;
        private List<Action> _actions;

        public DisposeAction()
        {
        }

        public DisposeAction(Action action)
        {
            _singleAction = action;
        }

        public DisposeAction Attach(Action action)
        {
            if (action == null)
                return this;

            if (_singleAction == null)
            {
                _singleAction = action;
            }
            else
            {
                _actions ??= new();
                _actions.Add(action);
            }
            return this;
        }

        public void Dispose()
        {
            _singleAction?.Invoke();
            if (_actions != null)
            {
                foreach (var action in _actions)
                {
                    action();
                }
            }
            Reset();
        }

        public void Reset()
        {
            _singleAction = null;
            _actions = null;
        }
    }
}