const CALENDER_ID = "22326mbg1cbd88bi5s7e9cklbg@group.calendar.google.com";
const EVENT_NAME = "弦巻マキ深夜の真剣お絵描き60分勝負";

function onTrigger(arg: GoogleAppsScript.Events.AppsScriptEvent)
{
	if(arg == undefined)
	{
		console.log("need launch from trigger");
		return;
	}
	else
	{
		console.log(`triggered ${arg.triggerUid}`);
	}

	const date = new Date();
	const span = 5;
	const searchStart = new Date(date.getFullYear(), date.getMonth() - 1, date.getDate());
	const searchEnd = new Date(date.getFullYear(), date.getMonth() + 1, date.getDate());
	const events = CalendarApp.getCalendarById(CALENDER_ID)
		.getEvents(searchStart, searchEnd)
		.filter(ev => ev.getTitle() == EVENT_NAME);

	const previousEvent = events.reverse().find(i => toDateTick(i.getStartTime()) < toDateTick(date));
	const nextEvent = events.find(i => toDateTick(i.getEndTime()) > toDateTick(date));
	const toDayEvent = events.find(i => toDateTick(i.getStartTime()) == toDateTick(date) || toDateTick(i.getEndTime()) == toDateTick(date));
	const runningEvent = events.find(i => approximately(i.getStartTime(), span, date) >= 0  && approximately(i.getEndTime(), span, date) <= 0);
	console.log(`previousEvent: ${previousEvent?.getTitle()} ${previousEvent?.getStartTime()}-${previousEvent?.getEndTime()}`);
	console.log(`nextEvent: ${nextEvent?.getTitle()} ${nextEvent?.getStartTime()}-${nextEvent?.getEndTime()}`);
	console.log(`toDayEvent: ${toDayEvent?.getTitle()} ${toDayEvent?.getStartTime()}-${toDayEvent?.getEndTime()}`);
	console.log(`runningEvent: ${runningEvent?.getTitle()} ${runningEvent?.getStartTime()}-${runningEvent?.getEndTime()}`);

	const morningTime = new Date(toDayEvent?.getStartTime()?.getFullYear(), toDayEvent?.getStartTime()?.getMonth(), toDayEvent?.getStartTime()?.getDate(), 9, 30);
	const accumulateTime = new Date(previousEvent?.getStartTime()?.getFullYear(), previousEvent?.getStartTime()?.getMonth(), previousEvent?.getStartTime()?.getDate(), 36, 45);
	if(toDayEvent != null
		&& 0 == approximately(morningTime, span, date))
	{
		run("NotificationMorning", morningTime, toDayEvent.getStartTime(), nextEvent?.getStartTime());
	}
	else if(runningEvent != null
		&& 0 == approximately(runningEvent.getStartTime(), span, date))
	{
		run("NotificationStart", runningEvent.getStartTime(), runningEvent.getStartTime(), nextEvent?.getStartTime());
	}
	else if(runningEvent != null
		&& 0 == approximately(runningEvent.getEndTime(), span, date))
	{
		run("NotificationFinish", runningEvent.getEndTime(), runningEvent.getStartTime(), nextEvent?.getStartTime());
	}
	else if(previousEvent?.getStartTime()?.getDate() == date.getDate() - 1
		&& 0 == approximately(accumulateTime, span, date))
	{
		run("AccumulationPosts", accumulateTime, previousEvent.getStartTime(), nextEvent?.getStartTime());
	}
	else
	{
		console.log("no task");
	}
}

function run(workflowName : string, actionDate : Date | GoogleAppsScript.Base.Date, eventDate : Date | GoogleAppsScript.Base.Date, nextDate : Date | GoogleAppsScript.Base.Date) : void
{
	console.log(`workflow:${workflowName}, actionDate: ${actionDate}, eventDate:${eventDate}, nextDate:${nextDate}`);
	const headers = {
			Authorization : `token ${getGithubToken()}`,
			Accept : "application/vnd.github.v3+json"
	};
	const workflowsResponse = UrlFetchApp.fetch(`https://api.github.com/repos/wallstudio/MakiOneDrawing/actions/workflows`, { method: "get", headers: headers });
	const workflows : any[] = JSON.parse(workflowsResponse.getContentText()).workflows;
	const workflow = workflows.find(w => w.name == "Main");

	let res = UrlFetchApp.fetch(`https://api.github.com/repos/wallstudio/MakiOneDrawing/actions/workflows/${workflow.id}/dispatches`,
	{
		method: "post",
		headers: headers,
		payload: JSON.stringify({ ref: "master", inputs:
		{
			command: workflowName,
			actionDate : `${actionDate.getFullYear()}/${actionDate.getMonth() + 1}/${actionDate.getDate()} ${actionDate.getHours()}:${actionDate.getMinutes()} +09:00`,
			eventDate: `${eventDate.getFullYear()}/${eventDate.getMonth() + 1}/${eventDate.getDate()} ${eventDate.getHours()}:${eventDate.getMinutes()} +09:00`,
			nextDate: `${nextDate?.getFullYear()}/${nextDate?.getMonth() + 1}/${nextDate?.getDate()} ${nextDate?.getHours()}:${nextDate?.getMinutes()} +09:00`,
			general: "From GAS trigger."
		}})
	});
	console.log(`${res.getResponseCode()}`);
}

function approximately(ref : Date | GoogleAppsScript.Base.Date, span : number, date : Date) : number
{
	const dateStart = new Date(date.getFullYear(), date.getMonth(), date.getDate(), date.getHours(), date.getMinutes());
	const dateEnd = new Date(date.getFullYear(), date.getMonth(), date.getDate(), date.getHours(), date.getMinutes() + span);
	if(dateStart <= ref && ref < dateEnd)
	{
		return 0;
	}
	else if (ref < dateStart)
	{
		return +1;
	}
	else if (dateEnd <= ref)
	{
		return -1;
	}
}

function makeSchedules()
{
	const description = "詳細はこちら！\nhttps://wallstudio.github.io/MakiOneDrawing/";
	const date = new Date();
	for (
		let d = date;
		d.getTime() < new Date(date.getFullYear(), date.getMonth() + 2).getTime(); // とりあえず来月末まで
		d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1))
	{
		if(d.getDate() % 10 != 3) continue;

		const start = new Date(d.getFullYear(), d.getMonth(), d.getDate(), 22);
		const end = new Date(d.getFullYear(), d.getMonth(), d.getDate(), 25);
		CalendarApp.getCalendarById(CALENDER_ID).createEvent(EVENT_NAME, start, end, { "description" : description});
		console.log(`Scheduled at ${start.toLocaleString()}`);
	}
}

function toDate(date : Date | GoogleAppsScript.Base.Date) : Date
{
	return new Date(date.toLocaleDateString());
}

function toDateTick(date : Date | GoogleAppsScript.Base.Date) : number
{
	return toDate(date).getTime();
}
