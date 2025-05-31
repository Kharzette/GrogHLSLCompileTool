using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

//entry points for shaders
Dictionary<string, List<string>>	mVSEntryPoints	=new Dictionary<string, List<string>>();
Dictionary<string, List<string>>	mPSEntryPoints	=new Dictionary<string, List<string>>();
Dictionary<string, List<string>>	mCSEntryPoints	=new Dictionary<string, List<string>>();


string	VersionString(string sm)
{
	switch(sm)
	{
		case	"SM2":
		return	"2_0";
		case	"SM4":
		return	"4_0";
		case	"SM41":
		return	"4_1";
		case	"SM5":
		return	"5_0";
	}
	return	"69";
}

string	ProfileFromSM(string sm, ShaderEntryType set)
{
	switch(set)
	{
		case	ShaderEntryType.Compute:
		return	"cs_" + VersionString(sm);
		case	ShaderEntryType.Geometry:
		return	"gs_" + VersionString(sm);
		case	ShaderEntryType.Pixel:
		return	"ps_" + VersionString(sm);
		case	ShaderEntryType.Vertex:
		return	"vs_" + VersionString(sm);
		case	ShaderEntryType.Domain:
		return	"ds_" + VersionString(sm);
		case	ShaderEntryType.Hull:
		return	"hs_" + VersionString(sm);
	}
	return	"broken";
}


string	FixWinePaths(string winePath)
{
	if(!winePath.StartsWith("Z:"))
	{
		//maybe ok?
		return	winePath;
	}

	//fullpath
//	return	winePath.Substring(2);

	//relative
	//locate final \
	int	slashPos	=winePath.LastIndexOf('\\');

	//in my projects, usually GrogLibs is a submodule,
	//so the path to the shader file would be GrogLibs/ShaderLib/blah,
	//but also sometimes I build just the libs in which case it is
	//just like ShaderLib/blah

#if false	//running from GrogLibs
	string	path	="ShaderLib/" + winePath.Substring(slashPos + 1);
#else		//running with GrogLibs submodule
	string	path	="GrogLibsC/ShaderLib/" + winePath.Substring(slashPos + 1);
#endif

//	return	"${workspaceFolder}/ShaderLib/" + winePath.Substring(slashPos + 1);
	return	path;
}


//vscode wants 1 based columns
//fxc seems to sometimes use 0, sometimes 1, very inconsistent
//forgive all the commented out prints, I couldn't use a debugger for this
string	FixErrorColumns(string err)
{
	//find the first , this will be at the columns
	int	commaPos	=err.IndexOf(',');
	if(commaPos == -1)
	{
		Console.WriteLine("No Column Error: " + err);
		//no columns?
		return	err;
	}

	//find the dash between columns
	int	dashPos	=err.IndexOf('-', commaPos);

	//final ) pos
	int	parPos	=err.IndexOf(")");

	Console.WriteLine("Pos:" + commaPos + "," + dashPos + "," + parPos);

	string	firstCol, secCol;
	int		startCol, endCol;
	bool	bWorked;

	//is this a column range or just one column val?
	if(dashPos == -1)
	{
		Console.WriteLine("Single Column");

		if(parPos < 0 || commaPos < 0)
		{
			//something went wrong, just return original string
			return	err;
		}

		firstCol	=err.Substring(commaPos + 1, parPos - commaPos - 1);

		bWorked	=int.TryParse(firstCol, out startCol);
		if(!bWorked)
		{
			Console.WriteLine("failed parsing: " + firstCol);
			//something went wrong, just return original string
			return	err;
		}

		//increment to match vscode's column start at one thing
		startCol++;

		//make a fake end column
		return	err.Substring(0, commaPos) + "," + startCol + "-" + startCol + err.Substring(parPos);
	}

//	Console.WriteLine("Two Column");

	firstCol	=err.Substring(commaPos + 1, dashPos - commaPos - 1);
	secCol		=err.Substring(dashPos + 1, parPos - dashPos - 1);

	bWorked	=int.TryParse(firstCol, out startCol);
	if(!bWorked)
	{
		Console.WriteLine("failed parsing: " + firstCol);
		//something went wrong, just return original string
		return	err;
	}

	bWorked	=int.TryParse(secCol, out endCol);
	if(!bWorked)
	{
		Console.WriteLine("failed parsing: " + secCol);
		//something went wrong, just return original string
		return	err;
	}

	//increment to match vscode's column start at one thing
	startCol++;
	endCol++;

	return	err.Substring(0, commaPos) + "," + startCol + "-" + endCol + err.Substring(parPos);
}


void FireFXCProcess(string srcPath, string fileName, string entryPoint, string mod, string profile)
{
	//kompile
	Process	proc	=new Process();

	//split off the first two letters of the profile
	string	profDir	=profile.Substring(0, 2);

	profDir	=profDir.ToUpperInvariant();

	string	command	="bin/fxc64.exe " +
		" /I " + srcPath +"/" +
		" /E " + entryPoint +
		" /T " + profile +
		" /nologo" +
		" /D " + mod + "=1" +
		" /Fo CompiledShaders/" + mod + "/" + profDir + "/" + entryPoint + ".cso" +
		" " + srcPath + "/" + fileName;

	Console.WriteLine("Compiling: " + entryPoint + " for " + mod + ":" + profile);
	//Console.WriteLine(command);

	proc.StartInfo	=new ProcessStartInfo("wine", command);

	proc.StartInfo.RedirectStandardInput	=true;
	proc.StartInfo.RedirectStandardOutput	=true;
	proc.StartInfo.RedirectStandardError	=true;
	proc.StartInfo.CreateNoWindow			=true;
	proc.StartInfo.UseShellExecute			=false;

	//error filters, stuff to ignore
	List<string>	errFilters	=new List<string>();
	errFilters.Add(":fixme:font:");
	errFilters.Add(":fixme:wineusb:");		//damn oculus sensors
	errFilters.Add("warning X3206:");		//this one is too common
	errFilters.Add(":err:rpc:");			//wine spam
	errFilters.Add("compilation failed");	//afraid this will interfere with problems tab

	proc.OutputDataReceived	+=new DataReceivedEventHandler((sender, e) =>
	{
		if(!String.IsNullOrEmpty(e.Data))
		{
			if(!e.Data.StartsWith("compilation object save succeeded;"))
			{
				Console.WriteLine(e.Data);
			}
		}
	});

	proc.ErrorDataReceived	+=new DataReceivedEventHandler((sender, e) =>
	{
		if(!String.IsNullOrEmpty(e.Data))
		{
			bool	bSkip	=false;
			foreach(string filter in errFilters)
			{
				if(e.Data.Contains(filter))
				{
					bSkip	=true;
					break;
				}
			}

			if(!bSkip)
			{

				string	goodPath	=FixWinePaths(e.Data);

				//add the profile into the error / warning
				//if not already present
				if(!goodPath.Contains(profile))
				{
					goodPath	+=" for " + profile;
				}
				Console.WriteLine(FixErrorColumns(goodPath));
			}
		}
	});

	proc.Start();

	proc.BeginOutputReadLine();
	proc.BeginErrorReadLine();

	proc.WaitForExit();
}

string StripExtension(string fileName)
{
	int	dotPos	=fileName.LastIndexOf('.');
	if(dotPos != -1)
	{
		return	fileName.Substring(0, dotPos);
	}
	return	fileName;
}

void ReadEntryPoints(StreamReader sr, Dictionary<string, List<string>> dict)
{
	string	curShader	="";
	for(;;)
	{
		string	line	=sr.ReadLine();
		if(line.StartsWith("//"))
		{
			continue;	//comment
		}

		//python style!
		if(line.StartsWith("\t"))
		{
			Debug.Assert(curShader != "");

			dict[curShader].Add(line.Trim());
		}
		else
		{
			curShader	=StripExtension(line);
			dict.Add(curShader, new List<string>());
		}

		if(sr.EndOfStream)
		{
			break;
		}
	}
}

//open entry points for VS
void LoadEntryPoints(string srcDir)
{
	Console.WriteLine(srcDir + "/VSEntryPoints.txt");

	if(!File.Exists(srcDir + "/VSEntryPoints.txt"))
	{
		Console.WriteLine("No VSEntryPoints.txt!");
		return;
	}
	if(!File.Exists(srcDir + "/PSEntryPoints.txt"))
	{
		return;
	}

	FileStream	fs	=new FileStream(srcDir + "/VSEntryPoints.txt", FileMode.Open, FileAccess.Read);
	if(fs == null)
	{
		return;
	}

	StreamReader	sr	=new StreamReader(fs);
	if(sr == null)
	{
		fs.Close();
		return;
	}

	ReadEntryPoints(sr, mVSEntryPoints);

	sr.Close();
	fs.Close();

	fs	=new FileStream(srcDir + "/PSEntryPoints.txt", FileMode.Open, FileAccess.Read);
	if(fs == null)
	{
		return;
	}

	sr	=new StreamReader(fs);
	if(sr == null)
	{
		fs.Close();
		return;
	}

	ReadEntryPoints(sr, mPSEntryPoints);

	sr.Close();
	fs.Close();

	if(!File.Exists(srcDir + "/CSEntryPoints.txt"))
	{
		return;
	}

	fs	=new FileStream(srcDir + "/CSEntryPoints.txt", FileMode.Open, FileAccess.Read);
	if(fs == null)
	{
		return;
	}

	sr	=new StreamReader(fs);
	if(sr == null)
	{
		fs.Close();
		return;
	}

	ReadEntryPoints(sr, mCSEntryPoints);

	sr.Close();
	fs.Close();
}

if(args.Length < 2)
{
	Console.WriteLine("Usage: tool shaderSourceDir SM5 (or whichever models you want)");
	return;
}

string	sourcePath	=args[0];

Console.WriteLine("Full shader source path: " + sourcePath);

Console.WriteLine("Current Dir: " + Directory.GetCurrentDirectory());

string	relSrc	=Path.GetRelativePath(".", sourcePath);

Console.WriteLine("Relative shader source path: " + relSrc);

List<string>	models	=new List<string>();

//args contains the macros to build with
//should be SM2 or SM5 etc
foreach(string arg in args)
{
	if(arg == args[0])
	{
		continue;	//skip 0
	}

	models.Add(arg);
}

Console.WriteLine("Reading entry points!");

LoadEntryPoints(relSrc);

//test print
/*
foreach(KeyValuePair<string, List<string>> eps in mVSEntryPoints)
{
	Console.WriteLine("File: " + eps.Key);

	foreach(string ep in eps.Value)
	{
		Console.WriteLine("\tEntryPoint: " + ep);
	}
}*/

DirectoryInfo	di	=new DirectoryInfo(relSrc);

//see what shaders are here
FileInfo[]	shfi	=di.GetFiles("*.hlsl", SearchOption.TopDirectoryOnly);

if(shfi.Length == 0)
{
	Console.WriteLine("No shaders found!");
	return;
}

//Blast the old directory.  This is needed because old compiled
//shaders will hang out there if the names changed.
Directory.Delete("CompiledShaders", true);

foreach(string mod in models)
{
	//ensure directories are set up
	if(!Directory.Exists("CompiledShaders"))
	{
		Directory.CreateDirectory("CompiledShaders");
	}

	if(!Directory.Exists("CompiledShaders/" + mod))
	{
		Directory.CreateDirectory("CompiledShaders/" + mod);
	}

	//traditionally on windows side I'd include a few extra bytes
	//at the beginning for the length and type of shader (vs, ps, etc)
	//but now I think I'll use a dir for that
	if(!Directory.Exists("CompiledShaders/" + mod + "/VS"))
	{
		Directory.CreateDirectory("CompiledShaders/" + mod + "/VS");
	}
	if(!Directory.Exists("CompiledShaders/" + mod + "/PS"))
	{
		Directory.CreateDirectory("CompiledShaders/" + mod + "/PS");
	}
	if(!Directory.Exists("CompiledShaders/" + mod + "/CS"))
	{
		Directory.CreateDirectory("CompiledShaders/" + mod + "/CS");
	}
	//add more if need CS etc

	foreach(FileInfo fi in shfi)
	{
		string	shdName	=StripExtension(fi.Name);

		string	profile	=ProfileFromSM(mod, ShaderEntryType.Vertex);
		if(mVSEntryPoints.ContainsKey(shdName))
		{
			//compile all VS entry points
			List<string>	eps	=mVSEntryPoints[shdName];
			Parallel.For(0, eps.Count, (i, state) =>
			{
				FireFXCProcess(relSrc, fi.Name, eps[i], mod, profile);
			});
		}
		else
		{
//			Console.WriteLine("No VS entry points for " + fi.Name);
		}

		//now PS entry points
		profile	=ProfileFromSM(mod, ShaderEntryType.Pixel);
		if(mPSEntryPoints.ContainsKey(shdName))
		{
			//compile all PS entry points
			List<string>	eps	=mPSEntryPoints[shdName];
			Parallel.For(0, eps.Count, (i, state) =>
			{
				FireFXCProcess(relSrc, fi.Name, eps[i], mod, profile);
			});
		}
		else
		{
//			Console.WriteLine("No PS entry points for " + fi.Name);
		}

		//now CS entry points
		profile	=ProfileFromSM(mod, ShaderEntryType.Compute);
		if(mCSEntryPoints.ContainsKey(shdName))
		{
			//compile all PS entry points
			List<string>	eps	=mCSEntryPoints[shdName];
			Parallel.For(0, eps.Count, (i, state) =>
			{
				FireFXCProcess(relSrc, fi.Name, eps[i], mod, profile);
			});
		}
		else
		{
//			Console.WriteLine("No PS entry points for " + fi.Name);
		}

		//don't really use the other kinds yet
	}

	Console.WriteLine("Copying entry points and layout files to CompiledShaders...");

	File.Copy(relSrc + "/VSEntryPoints.txt", "CompiledShaders/VSEntryPoints.txt", true);
	File.Copy(relSrc + "/PSEntryPoints.txt", "CompiledShaders/PSEntryPoints.txt", true);
	File.Copy(relSrc + "/CSEntryPoints.txt", "CompiledShaders/CSEntryPoints.txt", true);
	File.Copy(relSrc + "/EntryLayouts.txt", "CompiledShaders/EntryLayouts.txt", true);

	Console.WriteLine("Done!");
}

enum	ShaderEntryType
{
	None,
	Vertex		=1,
	Pixel		=2,
	Compute		=4,
	Geometry	=8,
	Hull		=16,
	Domain		=32
};
