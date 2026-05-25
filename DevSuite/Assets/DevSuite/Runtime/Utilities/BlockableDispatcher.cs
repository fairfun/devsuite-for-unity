using System;

namespace Ff.DevSuite
{
    internal class BlockableDispatcher
    {
        private readonly ValueStack<bool> _block;
        private readonly Action _action;

        public BlockableDispatcher(ValueStack<bool> block, Action action)
        {
            _block = block;
            _action = action;
        }

        private bool _needDispatch;
        private bool _isSubscribed;

        public void Dispatch()
        {
            _needDispatch = false;
            if (_block.Value)
            {
                if (!_isSubscribed)
                {
                    _block.OnChanged += HandleBlockChanged;
                    _isSubscribed = true;
                }
                _needDispatch = true;
                return;
            }
            _action?.Invoke();
        }

        private void HandleBlockChanged(bool locked)
        {
            if (locked)
            {
                return;
            }

            if (_needDispatch)
            {
                Dispatch();
            }
        }

        public void Reset()
        {
            _needDispatch = false;
            _isSubscribed = false;
            _block.OnChanged -= HandleBlockChanged;
        }
    }
}