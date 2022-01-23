class ScheduleTable {
	readonly entries: { id: string; pre: Date; begin: Date; end: Date; acc: Date; }[] = [];

	constructor(spreadSheetId: string) {
		const spreadsheet = SpreadsheetApp.openById(spreadSheetId);
		const schedule = spreadsheet.getSheetByName("schedule3");
		const labels = schedule.getRange(1, 1, 1, schedule.getLastColumn()).getDisplayValues()[0];
		const rows = schedule.getRange(2, 1, schedule.getLastRow(), schedule.getLastColumn()).getDisplayValues();
		for (const row of rows)
        {
            if(!row[labels.indexOf("Id")]
                || !row[labels.indexOf("PreTime")] || !row[labels.indexOf("BeginTime")] || !row[labels.indexOf("EndTime")] || !row[labels.indexOf("AccTime")]
                || !row[labels.indexOf("Theme1")] || !row[labels.indexOf("Theme2")])
            {
                continue;
            }

			const id = row[labels.indexOf("Id")] as string;
			const date = new Date(ScheduleTable.parseDateTick(id));
			const preTime = new Date(date.getTime() + ScheduleTable.parseTimeTick(row[labels.indexOf("PreTime")] as string));
			const beginTime = new Date(date.getTime() + ScheduleTable.parseTimeTick(row[labels.indexOf("BeginTime")] as string));
			const endTime = new Date(date.getTime() + ScheduleTable.parseTimeTick(row[labels.indexOf("EndTime")] as string));
			const accTime = new Date(date.getTime() + ScheduleTable.parseTimeTick(row[labels.indexOf("AccTime")] as string));
			this.entries.push({ id: id, pre: preTime, begin: beginTime, end: endTime, acc: accTime });
		}
	}

	static parseDateTick(input: string): number {
		const groups = input.match(/(?<year>\d+)\/(?<month>\d+)\/(?<date>\d+)/).groups;
		const year = parseInt(groups["year"]);
		const month = parseInt(groups["month"]);
		const date = parseInt(groups["date"]);
		return new Date(year, month - 1, date).getTime();
	}

	static parseTimeTick(input: string): number {
		const groups = input.match(/(?<hours>\d+)\:(?<minutes>\d+)/).groups;
		const hours = parseInt(groups["hours"]);
		const minutes = parseInt(groups["minutes"]);
		const epoch = new Date(0);
		return (hours * 60 + minutes) * 60 * 1000;
	}
}
