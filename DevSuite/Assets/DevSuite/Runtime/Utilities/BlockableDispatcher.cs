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

        public void Dispatch()
        {
            Reset();
            if (_block.Value)
            {
                _block.OnChanged += HandleBlockChanged;
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

            Dispatch();
        }

        public void Reset()
        {
            _block.OnChanged -= HandleBlockChanged;
        }
    }
}