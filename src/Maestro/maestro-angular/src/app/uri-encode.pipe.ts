import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'uriEncode'
})
export class UriEncodePipe implements PipeTransform {
  transform(value: string): string | undefined {
    if (value) {
      return encodeURIComponent(value);
    }
  }
}
