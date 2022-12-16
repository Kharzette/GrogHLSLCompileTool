using System.IO;
using System.Diagnostics;

	//entry points for shaders
Dictionary<string, List<string>>	mVSEntryPoints	=new Dictionary<string, List<string>>();
Dictionary<string, List<string>>	mPSEntryPoints	=new Dictionary<string, List<string>>();

//compiled shader bytecode
Dictionary<string, byte[]>	mVSCode	=new Dictionary<string, byte[]>();
Dictionary<string, byte[]>	mPSCode	=new Dictionary<string, byte[]>();
Dictionary<string, byte[]>	mGSCode	=new Dictionary<string, byte[]>();
Dictionary<string, byte[]>	mDSCode	=new Dictionary<string, byte[]>();
Dictionary<string, byte[]>	mHSCode	=new Dictionary<string, byte[]>();
Dictionary<string, byte[]>	mCSCode	=new Dictionary<string, byte[]>();


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


void FireFXCProcess(string fileName, string entryPoint, string mod, string profile)
{
	//kompile
	Process	proc	=new Process();

	proc.StartInfo.RedirectStandardOutput	=true;

	string	command	="bin/fxc64.exe " +
		" /I Shaders/" +
		" /E " + entryPoint +
		" /T " + profile +
		" /D " + mod + "=1" +
		" /Fo CompiledShaders/" + mod + "/" + entryPoint + ".cso" +
		" Shaders/" + fileName;

//	Console.WriteLine(command);

	proc.StartInfo	=new ProcessStartInfo("wine64", command);

	proc.OutputDataReceived	+=new DataReceivedEventHandler((sender, e) =>
	{
		if(!String.IsNullOrEmpty(e.Data))
		{
			Console.WriteLine(e.Data);
		}
	});

	proc.Start();
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

foreach(KeyValuePair<string, List<string>> eps in mVSEntryPoints)
{
	Console.WriteLine("File: " + eps.Key);

	foreach(string ep in eps.Value)
	{
		Console.WriteLine("\tEntryPoint: " + ep);
	}
}

//see what shaders are here
DirectoryInfo	di	=new DirectoryInfo(".");

FileInfo[]	shfi	=di.GetFiles("Shaders/*.hlsl", SearchOption.TopDirectoryOnly);

List<string>	models	=new List<string>();

models.Add("SM2");
models.Add("SM4");
models.Add("SM5");

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
			foreach(string ep in mVSEntryPoints[shdName])
			{
				FireFXCProcess(fi.Name, ep, mod, profile);
			}
		}
		else
		{
			Console.WriteLine("No VS entry points for " + fi.Name);
		}

		//now PS entry points
		profile	=ProfileFromSM(mod, ShaderEntryType.Pixel);
		if(mPSEntryPoints.ContainsKey(shdName))
		{
			//compile all PS entry points
			foreach(string ep in mPSEntryPoints[shdName])
			{
				FireFXCProcess(fi.Name, ep, mod, profile);
			}
		}
		else
		{
			Console.WriteLine("No PS entry points for " + fi.Name);
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
