// RPMusicPlayer - a music player for RasterPropMonitor in Kerbal Space Program 1.
// Copyright (C) 2026 Lukas Fülling <lukas@k40s.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Reflection;
using System.Runtime.InteropServices;

// The GUID matches the project GUID, so the COM type library id stays stable.
// The version below is what the plugin reports in game.
// Author and copyright show up in the dll's file properties and in KSP's
// plugin list, so keep them in step with LICENSE and the README.

[assembly: AssemblyTitle("RPMusicPlayer")]
[assembly: AssemblyDescription("A music player for RasterPropMonitor in Kerbal Space Program 1.")]
[assembly: AssemblyProduct("RPMusicPlayer")]
[assembly: AssemblyCompany("Lukas Fülling")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Lukas Fülling <lukas@k40s.net>")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("c7a9621b-d856-4708-8e5e-582c85fcf338")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]