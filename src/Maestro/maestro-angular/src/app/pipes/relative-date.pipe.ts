import { Pipe, PipeTransform } from '@angular/core';
import { formatRelative } from "date-fns";

@Pipe({
  name: 'relativeDate'
})
export class RelativeDatePipe implements PipeTransform {

  transform(value: Date): any {
    return formatRelative(value, new Date());
  }

}
