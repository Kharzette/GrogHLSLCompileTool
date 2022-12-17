using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

//entry points for shaders
Dictionary<string, List<string>>	mVSEntryPoints	=new Dictionary<string, List<string>>();
Dictionary<string, List<string>>	mPSEntryPoints	=new Dictionary<string, List<string>>();

List<string>	includeNames	=new List<string>();


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
//	return	"${workspaceFolder}/ShaderLib/" + winePath.Substring(slashPos + 1);
	return	"ShaderLib/" + winePath.Substring(slashPos + 1);
}


//vscode wants 1 based columns
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

//	Console.WriteLine("Pos:" + commaPos + "," + dashPos + "," + parPos);

	string	firstCol, secCol;
	int		startCol, endCol;
	bool	bWorked;

	//is this a column range or just one column val?
	if(dashPos == -1)
	{
//		Console.WriteLine("Single Column");
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


void FireFXCProcess(string fileName, string entryPoint, string mod, string profile)
{
	//kompile
	Process	proc	=new Process();

	string	command	="bin/fxc64.exe " +
		" /I Shaders/" +
		" /E " + entryPoint +
		" /T " + profile +
		" /nologo" +
		" /D " + mod + "=1" +
		" /Fo CompiledShaders/" + mod + "/" + entryPoint + ".cso" +
		" Shaders/" + fileName;

//	Console.WriteLine(command);

	proc.StartInfo	=new ProcessStartInfo("wine64", command);

	proc.StartInfo.RedirectStandardInput	=true;
	proc.StartInfo.RedirectStandardOutput	=true;
	proc.StartInfo.RedirectStandardError	=true;
	proc.StartInfo.CreateNoWindow			=true;
	proc.StartInfo.UseShellExecute			=false;

	//error filters, stuff to ignore
	List<string>	errFilters	=new List<string>();
	errFilters.Add(":fixme:font:");
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
void LoadEntryPoints()
{
	if(!File.Exists("Shaders/VSEntryPoints.txt"))
	{
		Console.WriteLine("No VSEntryPoints.txt!");
		return;
	}
	if(!File.Exists("Shaders/PSEntryPoints.txt"))
	{
		return;
	}

	FileStream	fs	=new FileStream("Shaders/VSEntryPoints.txt", FileMode.Open, FileAccess.Read);
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

	fs	=new FileStream("Shaders/PSEntryPoints.txt", FileMode.Open, FileAccess.Read);
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
}

Console.WriteLine("Reading entry points!");

LoadEntryPoints();

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

DirectoryInfo	di	=new DirectoryInfo(".");

//first grab a list of include files, sometimes the names get mangled
FileInfo[]	incfi	=di.GetFiles("Shaders/*.hlsli", SearchOption.TopDirectoryOnly);
foreach(FileInfo fi in incfi)
{
	includeNames.Add(fi.Name);
}

//see what shaders are here
FileInfo[]	shfi	=di.GetFiles("Shaders/*.hlsl", SearchOption.TopDirectoryOnly);

List<string>	models	=new List<string>();

//args contains the macros to build with
//should be SM2 or SM5 etc
foreach(string arg in args)
{
	models.Add(arg);
}

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
				FireFXCProcess(fi.Name, eps[i], mod, profile);
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
				FireFXCProcess(fi.Name, eps[i], mod, profile);
			});
		}
		else
		{
//			Console.WriteLine("No PS entry points for " + fi.Name);
		}

		//don't really use the other kinds yet
	}
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
