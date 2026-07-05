using System;
using System.Collections;
using System.Collections.Generic;
using Ff.DevSuite.Commands;
using UnityEngine;

namespace Ff.DevSuite
{
    public class DevSuiteCommandsApi
    {
        private readonly DevSuiteContext _context;

        public DevSuiteCommandsApi(DevSuiteContext context)
        {
            _context = context;
        }

        internal void AddCategory(CommandCategory category, bool silent = false)
        {
            var key = new CategoryKey(category.Id);
            if (_context.Categories.ContainsKey(key) && !silent)
                Debug.LogWarning($"Category with id {category.Id} was already added. It will be replaced with the new one.");
            _context.Categories[key] = category;
            _context.ApiCalledDispatcher.Dispatch();
        }

        internal void RemoveCategory(string categoryId)
        {
            if (_context.Categories.Remove(new CategoryKey(categoryId)))
                _context.ApiCalledDispatcher.Dispatch();
        }

        internal void AddGroup(CommandGroup group, bool silent = false)
        {
            var key = new GroupKey(group.Id, group.CategoryId);
            if (_context.Groups.ContainsKey(key) && !silent)
                Debug.LogWarning($"Group with id {group.Id} was already added. It will be replaced with the new one.");
            _context.Groups[key] = group;
            group.RegistrationOrder = _context.RegistrationOrderCounter++;
            _context.ApiCalledDispatcher.Dispatch();
        }

        internal void RemoveGroup(string id, string catagoryId)
        {
            var key = new GroupKey(id, catagoryId);
            if (_context.Groups.Remove(key))
                _context.ApiCalledDispatcher.Dispatch();
        }

        internal void AddCommand(Command command, bool silent = false)
        {
            var key = new CommandKey(command.Id, command.GroupId, command.CategoryId, command.TargetInstance);
            if (_context.Commands.ContainsKey(key) && !silent)
                Debug.LogWarning($"Command with id '{command.Id}' was already added. It will be replaced with the new one.");
            _context.Commands[key] = command;
            command.RegistrationOrder = _context.RegistrationOrderCounter++;
            _context.ApiCalledDispatcher.Dispatch();
        }

        internal void RemoveCommand(string id, string groupId, string categoryId, object instance)
        {
            var key = new CommandKey(id, groupId, categoryId, instance);
            if (_context.Commands.Remove(key))
                _context.ApiCalledDispatcher.Dispatch();
        }

        internal void AttachCommandUnit(CommandKey commandKey, BaseCommandUnit unit, bool silent = false)
        {
            if (!_context.Commands.ContainsKey(commandKey) && !silent)
            {
                Debug.LogWarning($"Command with id '{commandKey.Id}' was not registered");
                return;
            }

            var command = _context.Commands[commandKey];
            unit.RegistrationOrder = command.Units.Count;
            unit.AssignedToCommand = command;

            command.Units.Add(unit);
            _context.ValidateCommandUnit(unit);
            _context.ApiCalledDispatcher.Dispatch();
        }

        public void RegisterAdapter(CommandValueAdapter valueAdapter, bool silent = false)
        {
            var index = _context.CommandValueAdapters.BinaryLastIndex(a => a.Priority >= valueAdapter.Priority);
            _context.CommandValueAdapters.Insert(index + 1, valueAdapter);
            _context.InvalidateCache();
            _context.ApiCalledDispatcher.Dispatch();
        }

        public void UnregisterAdapter(CommandValueAdapter valueAdapter)
        {
            _context.CommandValueAdapters.Remove(valueAdapter);
            _context.InvalidateCache();
        }

        public void RegisterValuesProvider(CommandValuesProvider valuesProvider, bool silent = false)
        {
            if (_context.ValuesProviders.ContainsKey(valuesProvider.Type) && !silent)
                Debug.LogWarning($"Value provider for type '{valuesProvider.Type}' has been already added. Will override.");
            _context.ValuesProviders[valuesProvider.Type] = valuesProvider;
            _context.InvalidateCache();
            _context.ApiCalledDispatcher.Dispatch();
        }

        public void UnregisterValuesProvider(CommandValuesProvider valuesProvider)
        {
            _context.ValuesProviders.Remove(valuesProvider.Type);
            _context.InvalidateCache();
            _context.ApiCalledDispatcher.Dispatch();
        }

        public void RegisterTargetForFunctionsProvider(CommandFunctionsSourceProvider provider, bool silent = false)
        {
            if (_context.TargetsForFunctionsProviders.ContainsKey(provider.Type) && !silent)
                Debug.LogWarning($"Value provider for type '{provider.Type}' has been already added. Will override.");
            _context.TargetsForFunctionsProviders[provider.Type] = provider;
            _context.InvalidateCache();
            _context.ApiCalledDispatcher.Dispatch();
        }

        public void UnregisterTargetForFunctionsProvider(CommandFunctionsSourceProvider provider)
        {
            _context.TargetsForFunctionsProviders.Remove(provider.Type);
            _context.InvalidateCache();
            _context.ApiCalledDispatcher.Dispatch();
        }
    }
}
