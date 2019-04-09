import { Component, OnInit, Input, OnChanges } from '@angular/core';
import { formatRelative, formatDistance, differenceInMilliseconds, startOfMinute, addMinutes, startOfHour, addHours, differenceInMinutes, differenceInHours } from 'date-fns';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { onThe } from 'src/helpers';

@Component({
  selector: 'mc-time-ago',
  template: "{{ value$ | async }}",
})
export class TimeAgoComponent implements OnChanges {

  @Input() public value?: Date;

  public value$?: Observable<string>;

  constructor() { }

  ngOnChanges() {
    if (this.value) {
      const now = new Date();
      let timer: Observable<number>;
      if (differenceInMinutes(now, this.value) < 45) {
        timer = onThe("minute");
      } else if (differenceInHours(now, this.value) < 24)  {
        timer = onThe("hour");
      } else {
        timer = onThe("day");
      }
      this.value$ = timer.pipe(
        map(() => formatDistance(this.value!, new Date()) + " ago"),
      );
    } else {
      this.value$ = undefined;
    }
  }
}
