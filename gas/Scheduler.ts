var isDryMode = false;

function onTrigger(arg: GoogleAppsScript.Events.AppsScriptEvent)
{
	isDryMode = arg == undefined; // need launch from trigger
	console.log(`triggered ${arg?.triggerUid ?? "???"}`);

	const span = 5 * 60 * 1000;
	const now = new Date();
	const schedules = new ScheduleTable("1Un15MnW9Z2ChwSdsxdAVw495uSmJN4jBHngcBpYxo_0");

	let taskCount = 0;
	const commands: { id: string, cmd: string, timing: Date }[] = [];
	for (const schedule of schedules.entries)
	{
		if(schedule.pre.getTime()-span < now.getTime() && now.getTime() < schedule.pre.getTime())
			commands.push({ id: schedule.id, cmd: "NotificationMorning", timing: schedule.pre });
		else if(schedule.begin.getTime()-span < now.getTime() && now.getTime() < schedule.begin.getTime())
			commands.push({ id: schedule.id, cmd: "NotificationStart", timing: schedule.begin });
		else if(schedule.end.getTime()-span < now.getTime() && now.getTime() < schedule.end.getTime())
			commands.push({ id: schedule.id, cmd: "NotificationFinish", timing: schedule.end });
		else if(schedule.acc.getTime()-span < now.getTime() && now.getTime() < schedule.acc.getTime())
			commands.push({ id: schedule.id, cmd: "AccumulationPosts", timing: schedule.acc });
	}
	for (const command of commands)
	{
		run(command.id, command.cmd, command.timing);
	}

	console.log(`finish ${commands.length}`);
}

function run(eventId: string, workflowName: string, timing: Date) : void
{
	console.log(`id:${eventId}, workflowName:${workflowName}, timing:${timing.toString()}`);
	const headers = {
		Authorization : `token ${getGithubToken()}`,
		Accept : "application/vnd.github.v3+json"
	};
	const workflowsResponse = UrlFetchApp.fetch(`https://api.github.com/repos/wallstudio/MakiOneDrawing/actions/workflows`, { method: "get", headers: headers });
	const workflows : any[] = JSON.parse(workflowsResponse.getContentText()).workflows;
	const workflow = workflows.find(w => w.name == "Main");

	const payload = JSON.stringify({
		ref: "master",
		inputs: {
			eventDate: eventId,
			command: workflowName,
			actionDate : `${timing.getFullYear()}/${timing.getMonth() + 1}/${timing.getDate()} ${timing.getHours()}:${timing.getMinutes()} +09:00`,
			general: "From GAS trigger."
	}});
	console.log(`${payload}`);
	
	if(isDryMode) return;

	let res = UrlFetchApp.fetch(
		`https://api.github.com/repos/wallstudio/MakiOneDrawing/actions/workflows/${workflow.id}/dispatches`,
		{ method: "post", headers: headers, payload: payload});
	console.log(`${res.getResponseCode()}`);
}
