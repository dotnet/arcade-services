import { Injectable } from "@angular/core";
import { Observable, of } from "rxjs";
import { Channel } from 'src/maestro-client/models';
import { MaestroService } from 'src/maestro-client';
import { shareReplay } from 'rxjs/operators';

@Injectable({
  providedIn: "root",
})
export class ChannelService {
  private channels$: Observable<Channel[]>;

  public constructor(private maestro: MaestroService) {
    this.channels$ = maestro.channels.listChannelsAsync("product").pipe(
      shareReplay(1),
    );
  }

  public getChannels(): Observable<Channel[]> {
    return this.channels$;
  }
}
