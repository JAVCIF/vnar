# Third-Party Notices

VNAR includes, interoperates with, or optionally connects to third-party software and services. Those projects retain their own licenses and terms.

Self-contained release packages also include a `licenses` directory containing the license and notice files supplied by the .NET installation and restored SkiaSharp/runtime packages. Preserve that directory when redistributing a build.

## .NET and Windows Desktop runtime

Self-contained Windows builds include the .NET and WPF runtimes. Their license and third-party notices are included in the release's `licenses` directory.

Projects: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

## SkiaSharp

VNAR uses **SkiaSharp 4.151.1** for image compatibility and WebP normalization.

Project: https://github.com/mono/SkiaSharp  
License: MIT

Copyright (c) 2015-2016 Xamarin, Inc.  
Copyright (c) 2017-2018 Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Locale Emulator

VNAR interoperates with **Locale Emulator** as an external application and may optionally download its official GitHub release at the user's request. Locale Emulator is not bundled into VNAR's source tree or release packages.

Project: https://github.com/xupefei/Locale-Emulator  
License files: `COPYING` / `COPYING.LESSER` in the Locale Emulator repository (GPL/LGPL family; see that project for component-specific terms).

## VNDB

VNAR uses the VNDB API for visual novel metadata, cover artwork, and developer associations.

Website: https://vndb.org/  
API: https://api.vndb.org/kana

VNDB data and API usage are subject to VNDB's own terms and data license.

## SerpApi

Optional Google Images search inside VNAR can use a user-provided SerpApi key.

Website: https://serpapi.com/

SerpApi usage is subject to SerpApi's own terms and account limits.

## Game assets

VNAR can display artwork and executable icons supplied or selected by the user. Those assets are not licensed under VNAR's MIT License and remain the property of their respective copyright holders.
