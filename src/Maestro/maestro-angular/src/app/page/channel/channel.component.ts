import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from "@angular/router";
import { map, switchMap, tap } from 'rxjs/operators';

import { FlowGraph, Channel} from 'src/maestro-client/models';
import { Observable, of, timer, OperatorFunction } from 'rxjs';
import { statefulSwitchMap, StatefulResult, statefulPipe } from 'src/stateful';
import { ChannelService} from 'src/app/services/channel.service';

@Component({
  selector: 'mc-channel',
  templateUrl: './channel.component.html',
  styleUrls: ['./channel.component.scss']
})
export class ChannelComponent implements OnInit {

  constructor(private route: ActivatedRoute, private channelService: ChannelService) { }

  public graph$!: Observable<StatefulResult<FlowGraph>>;
  public channel$?: Observable<StatefulResult<Channel>>;
  public includeArcade: boolean = true;

  ngOnInit() {
    const channelId$ = this.route.paramMap.pipe(
      map(params => {
        const channelId = params.get("channelId");

        if ( channelId == null ) {
          throw new Error("channelId was null");
        } 

        return +channelId;
      }),
      tap(v => {
        console.log("Params: ", v);
      })
    );

    this.channel$ = channelId$.pipe(
      statefulPipe(
        statefulSwitchMap((id) => {
          return this.channelService.getChannel(id);
        })
      )
    )

    this.graph$ = channelId$.pipe(
      statefulPipe(
        statefulSwitchMap((id) => {
          return this.channelService.getFlowGraph(id);
        })
      )
    )
  }

}
