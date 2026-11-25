using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ApiConnectTool.Models;

public enum AssertionKind { StatusCode, Header, ResponseTime, Body, JsonPath, Schema }
public enum AssertionOperator { Equals, NotEquals, GreaterThan, LessThan, Exists, Contains, Matches }

public partial class Assertion : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private AssertionKind _kind = AssertionKind.StatusCode;
    [ObservableProperty] private string _target = string.Empty;
    [ObservableProperty] private AssertionOperator _operator = AssertionOperator.Equals;
    [ObservableProperty] private string _expectedValue = "200";
    [ObservableProperty] private string? _description;

    public Assertion Clone(bool newId = false) => new()
    {
        Id = newId ? Guid.NewGuid() : Id,
        IsEnabled = IsEnabled, Kind = Kind, Target = Target, Operator = Operator,
        ExpectedValue = ExpectedValue, Description = Description
    };
}
