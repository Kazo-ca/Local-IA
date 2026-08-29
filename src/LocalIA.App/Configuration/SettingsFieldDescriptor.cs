using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalIA.Core.Configuration;
using LocalIA.Core.Models;

namespace LocalIA.App.Configuration;

/// <summary>
/// Enveloppe réflexive autour d'UNE propriété d'un groupe de <see cref="ModelTierSettings"/>,
/// utilisée par une unique DataTemplate générique pour éditer les ~150 flags Ollama/llama.cpp
/// sans écrire un contrôle XAML dédié par champ.
/// </summary>
public sealed partial class SettingsFieldDescriptor : ObservableObject
{
    private readonly object _target;
    private readonly PropertyInfo _property;
    private readonly Type _underlyingType;
    private readonly Action? _onChanged;

    [ObservableProperty]
    private EngineKind currentEngine;

    public SettingsFieldDescriptor(object target, PropertyInfo property, EngineFlagAttribute attribute, EngineKind currentEngine, Action? onChanged = null)
    {
        _target = target;
        _property = property;
        Attribute = attribute;
        _underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        this.currentEngine = currentEngine;
        _onChanged = onChanged;

        IsBoolean = _underlyingType == typeof(bool);
        IsEnum = _underlyingType.IsEnum;
        IsTextLike = !IsBoolean && !IsEnum;
        IsInteger = _underlyingType == typeof(int);
        EnumOptions = IsEnum ? Enum.GetNames(_underlyingType) : null;
    }

    public EngineFlagAttribute Attribute { get; }

    public string DisplayName => Attribute.DisplayName ?? _property.Name;

    public bool IsBoolean { get; }
    public bool IsEnum { get; }
    public bool IsTextLike { get; }
    public bool IsInteger { get; }
    public IReadOnlyList<string>? EnumOptions { get; }

    public bool SupportedByOllama => Attribute.OllamaParameter is not null;
    public bool SupportedByLlamaCpp => Attribute.LlamaCppFlag is not null;

    public bool SupportedByCurrentEngine => CurrentEngine == EngineKind.Ollama ? SupportedByOllama : SupportedByLlamaCpp;

    public string FlagHint => CurrentEngine == EngineKind.Ollama
        ? Attribute.OllamaParameter is { } p ? $"PARAMETER {p}" : "Non supporté par Ollama"
        : Attribute.LlamaCppFlag is { } f ? f : "Non supporté par llama.cpp";

    public bool IsEditable => IsSet && SupportedByCurrentEngine;

    partial void OnCurrentEngineChanged(EngineKind value)
    {
        OnPropertyChanged(nameof(SupportedByCurrentEngine));
        OnPropertyChanged(nameof(FlagHint));
        OnPropertyChanged(nameof(IsEditable));
    }

    public bool IsSet
    {
        get => _property.GetValue(_target) is not null;
        set
        {
            if (value == IsSet)
            {
                return;
            }

            _property.SetValue(_target, value ? DefaultValue() : null);
            RaiseAllValueChanged();
        }
    }

    public string? TextValue
    {
        // InvariantCulture des deux côtés (lecture ET écriture) : sur une machine dont la culture
        // courante utilise la virgule décimale (fr-FR/fr-CA), afficher via ToString() nu puis
        // parser en InvariantCulture ci-dessous désynchronisait les deux — un double/int réaffiché
        // avec une virgule ne se re-parsait plus jamais, vidant silencieusement le champ.
        get => _property.GetValue(_target) switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
        set
        {
            if (!IsSet)
            {
                return;
            }

            object? converted = _underlyingType switch
            {
                _ when _underlyingType == typeof(int) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
                _ when _underlyingType == typeof(double) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
                _ => value,
            };

            _property.SetValue(_target, converted);
            OnPropertyChanged();
            _onChanged?.Invoke();
        }
    }

    public bool BoolValue
    {
        get => _property.GetValue(_target) is true;
        set
        {
            _property.SetValue(_target, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSet));
            _onChanged?.Invoke();
        }
    }

    public string? EnumValue
    {
        get => _property.GetValue(_target)?.ToString();
        set
        {
            if (value is null || !IsSet)
            {
                return;
            }

            _property.SetValue(_target, Enum.Parse(_underlyingType, value));
            OnPropertyChanged();
            _onChanged?.Invoke();
        }
    }

    private void RaiseAllValueChanged()
    {
        OnPropertyChanged(nameof(IsSet));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(EnumValue));
        OnPropertyChanged(nameof(IsEditable));
        _onChanged?.Invoke();
    }

    private object DefaultValue()
    {
        if (IsBoolean)
        {
            return true;
        }

        if (IsEnum)
        {
            return Enum.GetValues(_underlyingType).GetValue(0)!;
        }

        if (_underlyingType == typeof(int))
        {
            return 0;
        }

        if (_underlyingType == typeof(double))
        {
            return 0.0;
        }

        return "";
    }

    public static List<SettingsFieldDescriptor> FromGroup(object group, EngineKind currentEngine, Action? onChanged = null)
    {
        var descriptors = new List<SettingsFieldDescriptor>();
        foreach (var property in group.GetType().GetProperties())
        {
            var attribute = property.GetCustomAttribute<EngineFlagAttribute>(inherit: false);
            if (attribute is null || property.PropertyType == typeof(GpuLayerSpec))
            {
                continue;
            }

            descriptors.Add(new SettingsFieldDescriptor(group, property, attribute, currentEngine, onChanged));
        }

        return descriptors;
    }
}
