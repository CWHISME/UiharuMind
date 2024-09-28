namespace UiharuMind.Core.Core.Interfaces;

public interface ILlmModel
{
    string ModelName { get; }
    string ModelPath { get; }
    string ModelDescription { get; }
    string Port { get; }
}