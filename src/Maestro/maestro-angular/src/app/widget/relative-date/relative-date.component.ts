import { Component, OnInit, OnChanges, Input } from '@angular/core';
import { Observable, timer, of, concat } from 'rxjs';
import { formatRelative, differenceInMilliseconds, addDays, startOfDay } from 'date-fns';
import { map } from 'rxjs/operators';
import { onThe } from 'src/helpers';

@Component({
  selector: 'mc-relative-date',
  template: "{{ value$ | async }}",
})
export class RelativeDateComponent implements OnChanges {

  @Input() public value?: Date;

  public value$?: Observable<string>;

  constructor() { }

  ngOnChanges() {
    if (this.value) {
      this.value$ = onThe("day").pipe(
        map(() => formatRelative(this.value!, new Date())),
      );
    } else {
      this.value$ = undefined;
    }
  }

}
