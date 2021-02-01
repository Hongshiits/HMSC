#include<iostream>
#include<windows.h>
#include<io.h>
#include<fcntl.h>
#include <sys/stat.h>
using namespace std;
int a=0,total=0;
DWORD WINAPI Fun1(LPVOID lpParamter)
{
	Sleep(15000);
	system("python rcon_instruction_1.py");
}

DWORD WINAPI Fun2(LPVOID lpParamter)
{
	Sleep(15000);
	system("python rcon_instruction_2.py");
}

inline bool exists_test (const std::string& name) {
  struct stat buffer;   
  return (stat (name.c_str(), &buffer) == 0); 
}

int main()
{
	system("title HMSC_Server");
	char cmd[256],name[256]="server.jar";        //a: total commond  b:read file,starting server-core name
	
	
	
	
	int handle=open("core-name.ini",O_TEXT|O_CREAT|O_RDWR);
	if(read(handle,name,256)==0)
	{
		write(handle,name,strlen(name));
	}
	close(handle);
	
	
	
	sprintf(cmd,"java -jar %s nogui",name);
	char rcon_type[13];
	if(exists_test("rcon.py")){sprintf(rcon_type,"Have Found");}else{sprintf(rcon_type,"Not Found");};
	printf("Rcon :%s\n",rcon_type);
	if(exists_test(name)!=TRUE){printf("Server core %s Not Found,You can set in core_name.ini\n",name);system("PAUSE");return 0;}
	
	
	printf("boot commond :%s\n",cmd);
	
	printf("Warn:This is an endless loop, once the startup fails, will use a lot of system resources!!\n");
	printf("Would you want to continue?[press enter]\n");
	getchar();
	
	
	while(1)
	{
		system(cmd);
		a++; 
		total++;
		Sleep(1000);
		
		if(a>=5)
		{
			HANDLE hThread=CreateThread(NULL,0,Fun1,NULL,0,NULL);
			
			a=0;
		}
		if((total%10)==0)
		{
			HANDLE hThread=CreateThread(NULL,0,Fun2,NULL,0,NULL);
		}
		
	}
	
 } 
