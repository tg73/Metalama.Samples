﻿using Microsoft.Extensions.Logging;

namespace Metalama.Samples.Log4.Tests.GoodField;

internal class Foo
{
    private readonly ILogger _logger;

    public Foo( ILogger logger )
    {
        this._logger = logger;
    }

    [Log]
    public void Method() { }
}