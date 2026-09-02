using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.Models;

namespace RimTransAI.ViewModels;

public partial class TranslationApiProfileEditorViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _apiUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _autoCompleteApiUrl = true;
    [ObservableProperty] private int _requestTimeoutSeconds = 480;
    [ObservableProperty] private TranslationApiModel? _selectedModel;
    [ObservableProperty] private string _validationError = string.Empty;

    public string DialogTitle { get; }
    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);
    public ObservableCollection<TranslationApiModel> Models { get; } = new();
    public Window? CurrentWindow { get; set; }

    public TranslationApiProfileEditorViewModel(TranslationApiProfile? profile)
    {
        DialogTitle = profile == null ? "添加 API" : "编辑 API";
        Name = profile?.Name ?? string.Empty;
        IsEnabled = profile?.IsEnabled ?? true;
        ApiUrl = profile?.ApiUrl ?? string.Empty;
        ApiKey = profile?.ApiKey ?? string.Empty;
        AutoCompleteApiUrl = profile?.AutoCompleteApiUrl ?? true;
        RequestTimeoutSeconds = Math.Clamp(profile?.RequestTimeoutSeconds ?? 480, 30, 1800);

        foreach (var model in profile?.Models ?? [])
        {
            Models.Add(model.Clone());
        }

        SelectedModel = Models.FirstOrDefault();
    }

    [RelayCommand]
    private void AddModel()
    {
        var model = new TranslationApiModel();
        Models.Add(model);
        SelectedModel = model;
    }

    [RelayCommand]
    private void DeleteSelectedModel()
    {
        if (SelectedModel == null)
        {
            return;
        }

        var index = Models.IndexOf(SelectedModel);
        Models.Remove(SelectedModel);
        SelectedModel = Models.ElementAtOrDefault(Math.Min(index, Models.Count - 1));
    }

    [RelayCommand]
    private void MoveSelectedModelUp() => MoveSelectedModel(-1);

    [RelayCommand]
    private void MoveSelectedModelDown() => MoveSelectedModel(1);

    private void MoveSelectedModel(int offset)
    {
        if (SelectedModel == null)
        {
            return;
        }

        var currentIndex = Models.IndexOf(SelectedModel);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= Models.Count)
        {
            return;
        }

        Models.Move(currentIndex, targetIndex);
    }

    [RelayCommand]
    private void Save()
    {
        var result = new TranslationApiProfile
        {
            Name = Name.Trim(),
            IsEnabled = IsEnabled,
            ApiUrl = ApiUrl.Trim(),
            ApiKey = ApiKey.Trim(),
            AutoCompleteApiUrl = AutoCompleteApiUrl,
            RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 30, 1800),
            Models = Models
                .Select(model => new TranslationApiModel
                {
                    ModelId = model.ModelId.Trim(),
                    IsEnabled = model.IsEnabled
                })
                .ToList()
        };

        var errors = Validate(result);
        if (errors.Count > 0)
        {
            ValidationError = string.Join("、", errors);
            OnPropertyChanged(nameof(HasValidationError));
            return;
        }

        ValidationError = string.Empty;
        OnPropertyChanged(nameof(HasValidationError));
        CurrentWindow?.Close(result);
    }

    private static List<string> Validate(TranslationApiProfile profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("请输入 API 名称");
        }

        if (profile.Models.Any(model => string.IsNullOrWhiteSpace(model.ModelId)))
        {
            errors.Add("模型 ID 不能为空");
        }

        var hasDuplicateModel = profile.Models
            .Select(model => model.ModelId)
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .GroupBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
        if (hasDuplicateModel)
        {
            errors.Add("模型 ID 不能重复");
        }

        if (!profile.IsEnabled)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(profile.ApiUrl))
        {
            errors.Add("请输入 API 地址");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            errors.Add("请输入 API Key");
        }

        if (!profile.Models.Any(model => model.IsEnabled))
        {
            errors.Add("请启用至少一个模型");
        }

        return errors;
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close(null);
    }
}
